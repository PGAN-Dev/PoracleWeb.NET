using Pgan.PoracleWebNet.Core.Models;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Pgan.PoracleWebNet.Core.Abstractions.Services;

namespace Pgan.PoracleWebNet.Core.Services;

public partial class PoracleTrackingProxy(
    HttpClient httpClient,
    IConfiguration configuration,
    IPoracleServerProfileService serverProfile,
    IPoracleV2SchemaService v2Schema,
    IInvasionGruntNameService gruntNames,
    IMemoryCache cache,
    ILogger<PoracleTrackingProxy> logger) : IPoracleTrackingProxy
{
    /// <summary>
    /// Set when the v2 route for one type answered gin's plaintext 404, meaning this server does not carry
    /// it whatever its version said. Held for as long as the server profile is cached, so an upgrade is
    /// picked up on the same clock as everything else version-gated.
    /// </summary>
    /// <remarks>
    /// Keyed per type on purpose. A single flag let one 404 from one route drop every type back to v1 —
    /// which for <c>incident</c>, whose only surface is v2, would mean the type vanishing rather than
    /// degrading.
    /// </remarks>
    private static string V2AbsentCacheKey(string type) => $"poracle:v2-tracking-absent:{type}";

    private static readonly TimeSpan V2AbsentFor = TimeSpan.FromMinutes(5);

    private const string V2BoundsCacheKey = "poracle:v2-schema-bounds";

    private static readonly TimeSpan V2BoundsFor = TimeSpan.FromMinutes(10);

    /// <summary>
    /// The Pokestop event rules (<c>kecleon</c>, <c>gold-stop</c>, <c>showcase</c>) live in the invasion table,
    /// and the invasion read folds them in as v1 did, but v2's invasion routes filter them out
    /// (<c>isEventGruntType</c>): a single delete 404s and a bulk delete skips them. They are reached as
    /// <c>incident</c>.
    /// </summary>
    private const string InvasionType = "invasion";

    private const string IncidentType = "incident";

    /// <summary>
    /// PoracleNG answers 404 for a user that no longer exists; that is a dead session, not a server fault.
    /// </summary>
    /// <remarks>
    /// #584 fixed this on the human proxy only, so the alarm lists, dashboard, cleaning and profile
    /// overview kept returning 500 for a deleted account and the SPA -- which signs out on 401 -- left the
    /// user in a broken app. See #595.
    /// </remarks>
    private static void EnsureAccountStillExists(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new AccountGoneException();
        }
    }

    private static string Encode(string id) => Uri.EscapeDataString(id);
    private readonly HttpClient _httpClient = httpClient;
    private readonly string _apiAddress = configuration["Poracle:ApiAddress"] ?? string.Empty;
    private readonly string _apiSecret = configuration["Poracle:ApiSecret"] ?? string.Empty;

    private readonly IPoracleServerProfileService _serverProfile = serverProfile;
    private readonly IPoracleV2SchemaService _v2Schema = v2Schema;
    private readonly IInvasionGruntNameService _gruntNames = gruntNames;
    private readonly IMemoryCache _cache = cache;
    private readonly ILogger<PoracleTrackingProxy> _logger = logger;

    public async Task<TrackingCreateResult> CreateAsync(string type, string userId, JsonElement body)
    {
        var bodyText = body.GetRawText();
        LogCreateRequest(this._logger, type, userId, bodyText);
        var request = this.CreateRequest(HttpMethod.Post, $"{this._apiAddress}/api/tracking/{type}/{Encode(userId)}?silent=true");
        request.Content = new StringContent(bodyText, Encoding.UTF8, "application/json");

        var response = await this._httpClient.SendAsync(request);

        // A 400 from PoracleNG is the caller's problem, not the server's. EnsureSuccessStatusCode threw
        // an HttpRequestException that the global handler flattened into 500 "An unexpected error
        // occurred", so the user was told the server broke instead of what was wrong with their input,
        // and it was logged as a fault. Pass the explanation through as a 400. See #539.
        //
        // 422 is the same refusal wearing a different number: PoracleNG 5.2.1 moved several validation
        // 400s to 422 when it adopted RFC 9457. Matching only 400 would have re-opened #539 on every
        // one of them.
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity)
        {
            throw new AlarmValidationException(await ExtractMessageAsync(response));
        }

        EnsureAccountStillExists(response);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        LogCreateResponse(this._logger, type, userId, json);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var newUids = new List<long>();
        if (root.TryGetProperty("newUids", out var uidsEl) && uidsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var uid in uidsEl.EnumerateArray())
            {
                newUids.Add(uid.GetInt64());
            }
        }

        return new TrackingCreateResult(
            newUids,
            root.TryGetProperty("alreadyPresent", out var ap) ? ap.GetInt32() : 0,
            root.TryGetProperty("updates", out var upd) ? upd.GetInt32() : 0,
            root.TryGetProperty("insert", out var ins) ? ins.GetInt32() : 0);
    }

    /// <inheritdoc />
    public async Task<TrackingUpdateResult?> TryReplaceV2Async(
        string type, string userId, int uid, JsonElement body)
    {
        if (uid <= 0)
        {
            return null;
        }

        var result = await this.TryReplaceV2CoreAsync(type, userId, uid, body);
        if (result is null)
        {
            // Every null means "use the v1 write", and v1 blanks a set ping unconditionally (verified live
            // on 5.4.0, create and uid-upsert alike): a missing route, an invasion v2 will not take, a row
            // the translator declines. Refuse rather than lose a mention.
            RefuseIfV1WouldDropPing(body);
        }

        return result;
    }

    private static void RefuseIfV1WouldDropPing(JsonElement body)
    {
        if (body.ValueKind == JsonValueKind.Object
            && body.TryGetProperty("ping", out var ping)
            && ping.ValueKind == JsonValueKind.String
            && !string.IsNullOrEmpty(ping.GetString()))
        {
            throw new AlarmValidationException(
                "This alarm has a role or user mention set, and the connected PoracleNG can't preserve it "
                + "through this edit. Nothing was changed. Removing the mention first will let this edit through.");
        }
    }

    private async Task<TrackingUpdateResult?> TryReplaceV2CoreAsync(
        string type, string userId, int uid, JsonElement body)
    {

        if (!this.ShouldTryV2(type) || !await this.ServerCarriesV2Async(type))
        {
            return null;
        }

        if (await this.InvasionUnsendableReasonAsync(type, body) is { } invasionReason)
        {
            // Not a fault, same as an untranslatable field: the row goes to v1, which stores whatever it
            // is given, and the user's edit succeeds.
            LogV2Untranslatable(this._logger, type, uid, invasionReason);
            return null;
        }

        var (bounds, declared) = await this.ServerRuleSchemaAsync(type);

        if (!TrackingV2Translator.TryTranslate(type, body, bounds, declared, out var v2Body, out var unsupported))
        {
            // Not a fault. The row carries something v2 has no faithful place for, so it goes to v1,
            // which stores whatever it is given. See TrackingV2Translator.
            LogV2Untranslatable(this._logger, type, uid, unsupported ?? "unknown");
            return null;
        }

        return await this.PutV2Async(type, userId, uid, v2Body);
    }

    /// <inheritdoc />
    public async Task<TrackingUpdateResult> UpdateByUidAsync(
        string type, string userId, int uid, JsonElement body)
    {
        if (await this.TryReplaceV2Async(type, userId, uid, body) is { } replaced)
        {
            return replaced;
        }

        // v1: an update is a create carrying the uid, which PoracleNG upserts. Byte-identical to what
        // PoracleWeb has always sent, which is what makes 5.1.0 a genuine no-change.
        var created = await this.CreateAsync(type, userId, body);
        return new TrackingUpdateResult(created.PrimaryUid ?? uid, false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// v2 only. An invasion uid that invasion does not hold is tried as an incident, since the invasion read
    /// hands out Pokestop event uids too.
    /// </para>
    /// <para>
    /// v2 scopes the uid to the active profile, where v1 deleted the human's uid whatever profile it sat on.
    /// Quick-pick remove relies on that: its tracked uids can sit on a profile PoracleNG's scheduler has since
    /// switched away from. So a uid the active profile does not hold is looked up once across every profile
    /// (<see cref="DeleteOnOtherProfilesAsync"/>) and deleted there. Only a uid no profile holds is already
    /// gone, logged rather than thrown, as v1 treated a uid it could not find. A delete the active profile
    /// takes makes no extra read.
    /// </para>
    /// </remarks>
    public async Task DeleteByUidAsync(string type, string userId, int uid)
    {
        if (await this.DeleteOneV2Async(type, userId, uid, profileNo: null))
        {
            return;
        }

        if (type == InvasionType && await this.DeleteOneV2Async(IncidentType, userId, uid, profileNo: null))
        {
            return;
        }

        if ((await this.DeleteOnOtherProfilesAsync(type, userId, [uid], singleRoute: true)).Count > 0)
        {
            LogDeleteAlreadyGone(this._logger, type, uid);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// v2 only, with the uids in a <c>{"uids":[...]}</c> body, so no list is too long for it. v2 skips a uid
    /// the active profile does not hold and answers 200. What invasion left goes to <c>incident</c> (see
    /// <see cref="InvasionType"/>), what is still left is looked up on the human's other profiles and deleted
    /// there, as on the single form, and only what no profile holds is logged. Nothing goes to v1.
    /// </remarks>
    public async Task BulkDeleteByUidsAsync(string type, string userId, IEnumerable<int> uids)
    {
        var requested = uids.Distinct().ToList();
        if (requested.Count == 0)
        {
            return;
        }

        var remaining = requested.Except(await this.DeleteManyV2Async(type, userId, requested, profileNo: null)).ToList();

        if (type == InvasionType && remaining.Count > 0)
        {
            remaining = remaining.Except(await this.DeleteManyV2Async(IncidentType, userId, remaining, profileNo: null)).ToList();
        }

        if (remaining.Count > 0)
        {
            remaining = await this.DeleteOnOtherProfilesAsync(type, userId, remaining, singleRoute: false);
        }

        if (remaining.Count > 0)
        {
            LogBulkDeleteLeftUids(this._logger, type, userId, new UidList(remaining));
        }
    }

    /// <summary>
    /// Deletes the uids the active profile did not hold from whichever of the human's profiles holds them,
    /// with <c>?profile=N</c>. One all-profiles read places them; an invasion uid whose row is a Pokestop
    /// event (the read folds incident into invasion) is deleted as an incident.
    /// </summary>
    /// <param name="singleRoute">Re-issue on the single-rule route, as the caller's own delete was.</param>
    /// <returns>The uids no profile holds.</returns>
    private async Task<List<int>> DeleteOnOtherProfilesAsync(string type, string userId, List<int> uids, bool singleRoute)
    {
        var all = await this.GetAllTrackingAllProfilesAsync(userId);
        var snapshotKey = type == IncidentType ? InvasionType : type;
        var wanted = uids.ToHashSet();
        var placed = new Dictionary<(string Type, int Profile), List<int>>();

        if (all.ValueKind == JsonValueKind.Object
            && all.TryGetProperty(snapshotKey, out var rows)
            && rows.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in rows.EnumerateArray())
            {
                var uid = UidOf(row);
                if (!wanted.Remove(uid)
                    || !row.TryGetProperty("profile_no", out var no)
                    || no.ValueKind != JsonValueKind.Number)
                {
                    continue;
                }

                var isEvent = snapshotKey == InvasionType
                    && row.TryGetProperty("grunt_type", out var grunt)
                    && grunt.ValueKind == JsonValueKind.String
                    && PokestopEventTypes.IsEventName(grunt.GetString());

                // The snapshot folds events into invasion, so a plain grunt rule sharing an incident
                // uid is not the rule the caller named.
                if (type == IncidentType && !isEvent)
                {
                    continue;
                }

                var routeType = isEvent ? IncidentType : snapshotKey;

                var key = (routeType, no.GetInt32());
                if (!placed.TryGetValue(key, out var group))
                {
                    placed[key] = group = [];
                }

                group.Add(uid);
            }
        }

        var remaining = uids.ToList();
        foreach (var ((routeType, profileNo), group) in placed)
        {
            if (singleRoute)
            {
                if (await this.DeleteOneV2Async(routeType, userId, group[0], profileNo))
                {
                    remaining.Remove(group[0]);
                }

                continue;
            }

            var deleted = await this.DeleteManyV2Async(routeType, userId, group, profileNo);
            remaining.RemoveAll(deleted.Contains);
        }

        return remaining;
    }

    private static string ProfileQuery(int? profileNo) => profileNo is { } no ? $"&profile={no}" : string.Empty;

    /// <returns>True when PoracleNG deleted the rule; false when it holds no such rule for this human.</returns>
    private async Task<bool> DeleteOneV2Async(string type, string userId, int uid, int? profileNo)
    {
        var request = this.CreateRequest(
            HttpMethod.Delete,
            $"{this._apiAddress}/api/v2/humans/{Encode(userId)}/tracking/{type}/{uid}?silent=true{ProfileQuery(profileNo)}");

        var response = await this._httpClient.SendAsync(request);
        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        ThrowUnlessRuleNotFound(response, await response.Content.ReadAsStringAsync());
        return false;
    }

    /// <returns>The uids PoracleNG reports it deleted.</returns>
    private async Task<IReadOnlyList<int>> DeleteManyV2Async(string type, string userId, List<int> uids, int? profileNo)
    {
        var request = this.CreateRequest(
            HttpMethod.Delete,
            $"{this._apiAddress}/api/v2/humans/{Encode(userId)}/tracking/{type}?silent=true{ProfileQuery(profileNo)}");
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { uids }), Encoding.UTF8, "application/json");

        var response = await this._httpClient.SendAsync(request);
        var payload = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode == HttpStatusCode.UnprocessableEntity
                && payload.Contains("\"query.uid\"", StringComparison.Ordinal))
            {
                // Before 5.4.0 the bulk route required ?uid and had no body form, so it refuses the body by
                // naming the missing query parameter. That is a server too old, not a bad request.
                throw new PoracleUnsupportedException("deleting alarms", RequiredVersion);
            }

            if (response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity)
            {
                throw new PoracleRequestRefusedException(
                    PoracleProblemDetails.Describe(payload), (int)response.StatusCode);
            }

            ThrowUnlessRuleNotFound(response, payload);
            return [];
        }

        try
        {
            return DeletedUids(payload);
        }
        catch (JsonException)
        {
            // A 200 that is not the envelope. Nothing can be said about which rules went, so all of them
            // are reported as left; the log line is the honest answer, and a retry here would be a guess.
            return [];
        }
    }

    /// <summary>Joins uids only if the log line is actually rendered.</summary>
    private readonly record struct UidList(List<int> Uids)
    {
        public override string ToString() => string.Join(", ", this.Uids);
    }

    /// <summary>
    /// Returns when a failed v2 delete is PoracleNG saying it holds no such rule; throws for everything else.
    /// </summary>
    private static void ThrowUnlessRuleNotFound(HttpResponseMessage response, string payload)
    {
        switch (response.StatusCode)
        {
            case HttpStatusCode.NotFound when !PoracleProblemDetails.IsProblemJson(payload):
                // gin's plaintext "404 page not found": the route is absent, so this is not 5.4.0.
                throw new PoracleUnsupportedException("deleting alarms", RequiredVersion);

            case HttpStatusCode.NotFound when payload.Contains("human not found", StringComparison.Ordinal):
                throw new AccountGoneException();

            case HttpStatusCode.NotFound:
                return;

            default:
                response.EnsureSuccessStatusCode();
                return;
        }
    }

    public async Task<IReadOnlyList<int>> BulkDeleteByUidsOnProfileAsync(
        string type, string userId, IReadOnlyList<int> uids, int profileNo)
    {
        if (uids.Count == 0)
        {
            return [];
        }

        // The uids go in the body, as on the active-profile form. ?all_profiles is a 422 on this route,
        // so a profile number is always named.
        var request = this.CreateRequest(
            HttpMethod.Delete,
            $"{this._apiAddress}/api/v2/humans/{Encode(userId)}/tracking/{type}?silent=true&profile={profileNo}");
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { uids }), Encoding.UTF8, "application/json");

        var response = await this._httpClient.SendAsync(request);
        var payload = await response.Content.ReadAsStringAsync();

        if (response.IsSuccessStatusCode)
        {
            return DeletedUids(payload);
        }

        switch (response.StatusCode)
        {
            case HttpStatusCode.NotFound when !PoracleProblemDetails.IsProblemJson(payload):
                throw new PoracleUnsupportedException("deleting alarms on a profile", "PoracleNG 5.4.0");

            case HttpStatusCode.NotFound when payload.Contains("human not found", StringComparison.Ordinal):
                throw new AccountGoneException();

            case HttpStatusCode.NotFound:
            case HttpStatusCode.Conflict:
            case HttpStatusCode.BadRequest:
            case HttpStatusCode.UnprocessableEntity:
                throw new PoracleRequestRefusedException(
                    PoracleProblemDetails.Describe(payload), (int)response.StatusCode);

            default:
                response.EnsureSuccessStatusCode();
                return [];
        }
    }

    /// <summary>The uids in a v2 bulk-delete answer's <c>deleted</c> rules; null or absent is none.</summary>
    private static List<int> DeletedUids(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("deleted", out var deleted)
            || deleted.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return deleted.EnumerateArray()
            .Where(rule => rule.ValueKind == JsonValueKind.Object
                && rule.TryGetProperty("uid", out var uid)
                && uid.ValueKind == JsonValueKind.Number)
            .Select(rule => rule.GetProperty("uid").GetInt32())
            .ToList();
    }

    public async Task ReloadStateAsync()
    {
        var request = this.CreateRequest(HttpMethod.Get, $"{this._apiAddress}/api/reload");
        var response = await this._httpClient.SendAsync(request);
        EnsureAccountStillExists(response);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// The numeric limits THIS server declares for its own v2 rules, and every property each rule declares,
    /// read from the <c>/openapi.json</c> it publishes and cached for ten minutes.
    /// </summary>
    /// <remarks>
    /// Read from the server rather than shipped as a table because the two disagree. PoracleNG bounds all
    /// 55 numeric filter fields on an unreleased branch, and this site stores values outside ten of them --
    /// values PoracleNG itself wrote. Applying those limits to a server that does not enforce them was
    /// measured at 68% of one instance's Pokemon rules dropping off the v2 write path, for no benefit,
    /// because that server accepts every one of the values. A 5.2.1 publishes exactly one bound.
    /// <para>
    /// Unreachable or unparseable yields no bounds rather than all bounds, so a failed fetch leaves the
    /// write exactly as it is today and lets v2 answer for itself. It also yields no declared properties,
    /// so nothing the field tables do not model is carried through on the strength of a guess.
    /// </para>
    /// </remarks>
    private async Task<(IReadOnlyDictionary<string, TrackingV2Translator.Bound>? Bounds, IReadOnlySet<string>? Declared)>
        ServerRuleSchemaAsync(string type)
    {
        if (!this._cache.TryGetValue<ServerRuleSchemas>(V2BoundsCacheKey, out var schemas) || schemas is null)
        {
            string? document = null;

            try
            {
                document = await this._httpClient.GetStringAsync($"{this._apiAddress}/openapi.json");
            }
            catch (HttpRequestException exception)
            {
                LogV2BoundsUnavailable(this._logger, exception.Message);
            }
            catch (TaskCanceledException exception)
            {
                LogV2BoundsUnavailable(this._logger, exception.Message);
            }

            schemas = new ServerRuleSchemas(V2SchemaBounds.Parse(document), V2SchemaBounds.ParseDeclared(document));
            this._cache.Set(V2BoundsCacheKey, schemas, V2BoundsFor);
        }

        return (
            schemas.Bounds.TryGetValue(type, out var bounds) ? bounds : null,
            schemas.Declared.TryGetValue(type, out var declared) ? declared : null);
    }

    private sealed record ServerRuleSchemas(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, TrackingV2Translator.Bound>> Bounds,
        IReadOnlyDictionary<string, IReadOnlySet<string>> Declared);

    /// <summary>
    /// Why this invasion row must not go to v2, or null when it may.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two questions, both answered by the server rather than assumed. Whether it carries
    /// <c>grunt_type</c> at all -- 5.2.1 does not, and its only targeting fields cannot express a grunt
    /// name -- and whether it knows the particular name this row holds, because v2 validates
    /// <c>grunt_type</c> against its grunt masterdata and answers 422 for anything else.
    /// </para>
    /// <para>
    /// That second check is not belt-and-braces. 32 of 201 invasion rules in production carry a name v2
    /// refuses, all of them visible and editable in the invasion list because v1's read returns them
    /// where v2's does not. A 422 here would be an edit failing on a rule the user did not break, which
    /// is the shape of #835.
    /// </para>
    /// </remarks>
    private async Task<string?> InvasionUnsendableReasonAsync(string type, JsonElement row)
    {
        if (!string.Equals(type, "invasion", StringComparison.Ordinal))
        {
            return null;
        }

        if (!(await this._v2Schema.GetAsync()).InvasionGruntType)
        {
            return "this server's v2 invasion rule has no grunt_type";
        }

        if (row.ValueKind != JsonValueKind.Object
            || !row.TryGetProperty("grunt_type", out var stored)
            || stored.ValueKind != JsonValueKind.String
            || stored.GetString() is not { Length: > 0 } gruntType)
        {
            return "the row names no grunt_type";
        }

        var accepted = await this._gruntNames.GetAsync();

        return accepted.Contains(InvasionGruntNameService.Normalise(gruntType))
            ? null
            : $"this server's grunt masterdata does not list '{gruntType}'";
    }

    /// <summary>Whether this type and this deployment are in scope for the v2 write path at all.</summary>
    /// <remarks>
    /// Every type with a v2 table is in scope here, invasion included since #841. Invasion carries a
    /// second gate the others do not -- see <see cref="InvasionUnsendableReasonAsync"/> -- because its
    /// targeting field only exists on a server carrying PoracleNG PR #217, and even there only for grunt
    /// names that server's masterdata lists.
    /// </remarks>
    private bool ShouldTryV2(string type) =>
        TrackingV2Translator.Handles(type);

    /// <summary>
    /// Whether the server is believed to carry v2: its reported version is supported, and a plaintext 404
    /// has not already shown this type's route absent.
    /// </summary>
    private async Task<bool> ServerCarriesV2Async(string type)
    {
        if (this._cache.TryGetValue(V2AbsentCacheKey(type), out _))
        {
            return false;
        }

        var profile = await this._serverProfile.GetAsync();
        return profile.IsSupported;
    }

    /// <summary>
    /// Full-replaces one rule through <c>/api/v2</c>. Returns null -- and remembers it -- when the route is
    /// not there, so the caller can use v1 for this request instead of failing it.
    /// </summary>
    private async Task<TrackingUpdateResult?> PutV2Async(
        string type, string userId, int uid, JsonElement body)
    {
        var bodyText = body.GetRawText();
        LogV2UpdateRequest(this._logger, type, uid, userId, bodyText);

        var request = this.CreateRequest(
            HttpMethod.Put,
            $"{this._apiAddress}/api/v2/humans/{Encode(userId)}/tracking/{type}/{uid}?silent=true");
        request.Content = new StringContent(bodyText, Encoding.UTF8, "application/json");

        var response = await this._httpClient.SendAsync(request);
        var payload = await response.Content.ReadAsStringAsync();

        if (response.IsSuccessStatusCode)
        {
            LogV2UpdateResponse(this._logger, type, uid, userId, payload);
            return new TrackingUpdateResult(UidFromV2Envelope(payload) ?? uid, true);
        }

        switch (response.StatusCode)
        {
            case HttpStatusCode.NotFound when !PoracleProblemDetails.IsProblemJson(payload):
                // gin answers a missing route with plaintext "404 page not found", so the route does not
                // exist on this build whatever /health claimed. Verified against 5.1.0.
                this._cache.Set(V2AbsentCacheKey(type), true, V2AbsentFor);
                LogV2RouteAbsent(this._logger, type);
                return null;

            case HttpStatusCode.NotFound when payload.Contains("human not found", StringComparison.Ordinal):
                // Same meaning as the bare 404 on a v1 tracking read: the account is gone.
                throw new AccountGoneException();

            case HttpStatusCode.NotFound:
                throw new TrackingRuleNotFoundException(type, PoracleProblemDetails.Describe(payload));

            case HttpStatusCode.Conflict:
                // v2 refuses a replacement that would duplicate another rule outright, which is the guard
                // TrackingUpdateReconciler had to reconstruct from a 200 on v1. Its wording names the uid.
                throw new TrackingConflictException(type, PoracleProblemDetails.Describe(payload));

            case HttpStatusCode.UnprocessableEntity:
            case HttpStatusCode.BadRequest:
                // v2 validates at 422 where v1 used 400. Both are the request being wrong, not the server;
                // letting EnsureSuccessStatusCode throw would flatten them into an opaque 500. See #539.
                throw new AlarmValidationException(PoracleProblemDetails.Describe(payload));

            default:
                response.EnsureSuccessStatusCode();
                return null;
        }
    }

    /// <summary>
    /// The uid the replacement lives under. v2 answers <c>{created, updated, unchanged}</c> and a PUT
    /// reports under <c>updated</c> -- but reading all three costs nothing and stops a future shuffle
    /// silently returning the dead uid.
    /// </summary>
    private static int? UidFromV2Envelope(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var name in new[] { "updated", "created", "unchanged" })
            {
                if (!root.TryGetProperty(name, out var bucket) || bucket.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var rule in bucket.EnumerateArray())
                {
                    if (rule.ValueKind == JsonValueKind.Object
                        && rule.TryGetProperty("uid", out var value)
                        && value.ValueKind == JsonValueKind.Number
                        && value.TryGetInt32(out var found)
                        && found > 0)
                    {
                        return found;
                    }
                }
            }
        }
        catch (JsonException)
        {
            // A 200 that is not the envelope. The rule was written; we simply do not know its uid, and the
            // caller keeps the one it had rather than losing the edit.
        }

        return null;
    }

    /// <summary>
    /// Reads whatever explanation PoracleNG returned, falling back to something honest.
    /// </summary>
    /// <remarks>
    /// The same reader the v2 path uses. v1 and v2 disagree about the shape of an error -- v1 answers
    /// <c>{message, status}</c> and v2 answers RFC 9457 problem+json -- but one reader covers both,
    /// because the field names do not collide. Two readers would be two places to fix a wording bug,
    /// and one of them would eventually be the one nobody updated.
    /// </remarks>
    private static async Task<string> ExtractMessageAsync(HttpResponseMessage response)
        => PoracleProblemDetails.Describe(await response.Content.ReadAsStringAsync());

    private HttpRequestMessage CreateRequest(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        if (!string.IsNullOrEmpty(this._apiSecret))
        {
            request.Headers.Add("X-Poracle-Secret", this._apiSecret);
        }

        return request;
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "v2 update {Type} uid={Uid} for {UserId} request: {Body}")]
    private static partial void LogV2UpdateRequest(ILogger logger, string type, int uid, string userId, string body);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "v2 update {Type} uid={Uid} for {UserId} response: {Response}")]
    private static partial void LogV2UpdateResponse(ILogger logger, string type, int uid, string userId, string response);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Sending {Type} uid={Uid} to the v1 surface: v2 cannot carry it faithfully ({Reason}).")]
    private static partial void LogV2Untranslatable(ILogger logger, string type, int uid, string reason);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Could not read PoracleNG's /openapi.json ({Reason}); treating its v2 rules as unbounded, which is how they are sent today.")]
    private static partial void LogV2BoundsUnavailable(ILogger logger, string reason);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "PoracleNG has no /api/v2 {Type} route despite reporting a version that should carry it. Using v1.")]
    private static partial void LogV2RouteAbsent(ILogger logger, string type);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Delete {Type} uid={Uid}: no profile of this human holds that rule (all were searched); treating it as already gone.")]
    private static partial void LogDeleteAlreadyGone(ILogger logger, string type, int uid);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Bulk delete {Type} for {UserId}: PoracleNG did not report deleting uids {Uids} (already gone, or not on the active profile).")]
    private static partial void LogBulkDeleteLeftUids(ILogger logger, string type, string userId, UidList uids);

    [LoggerMessage(Level = LogLevel.Information, Message = "Create {Type} for {UserId} request: {Body}")]
    private static partial void LogCreateRequest(ILogger logger, string type, string userId, string body);

    [LoggerMessage(Level = LogLevel.Information, Message = "Create {Type} for {UserId} response: {Response}")]
    private static partial void LogCreateResponse(ILogger logger, string type, string userId, string response);
}
