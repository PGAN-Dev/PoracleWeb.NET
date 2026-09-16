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
    /// How long the rendered <c>?uid=1,2,3</c> list may get before a bulk delete goes to v1 instead.
    /// </summary>
    /// <remarks>
    /// v2 takes the uid list in the query string where v1 takes it as a JSON body, so past some length it
    /// stops being a request every proxy and server in the path will carry. Deleting every alarm on a
    /// heavy account reaches several hundred uids. 2000 sits under the most conservative limit in common
    /// use and costs nothing to respect, because v1's body has no equivalent ceiling.
    /// </remarks>
    private const int V2BulkDeleteMaxUidQueryLength = 2000;

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

    /// <summary>
    /// <c>auto</c> (the default) asks the server what it is; <c>v1</c> and <c>v2</c> pin it. An operator
    /// needs the override because a fork can carry v2 while reporting an older number, or the reverse,
    /// and a version probe cannot see either.
    /// </summary>
    private readonly string _trackingApiVersion =
        (configuration["Poracle:TrackingApiVersion"] ?? "auto").Trim().ToLowerInvariant();

    private readonly IPoracleServerProfileService _serverProfile = serverProfile;
    private readonly IPoracleV2SchemaService _v2Schema = v2Schema;
    private readonly IInvasionGruntNameService _gruntNames = gruntNames;
    private readonly IMemoryCache _cache = cache;
    private readonly ILogger<PoracleTrackingProxy> _logger = logger;

    public async Task<JsonElement> GetByUserAsync(string type, string userId)
    {
        var request = this.CreateRequest(HttpMethod.Get, $"{this._apiAddress}/api/tracking/{type}/{Encode(userId)}");
        var response = await this._httpClient.SendAsync(request);
        EnsureAccountStillExists(response);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        // PoracleNG returns { "pokemon": [...], ... } — extract the array by type key
        if (doc.RootElement.TryGetProperty(type, out var array))
        {
            return array.Clone();
        }

        return PoracleJsonHelper.EmptyArray;
    }

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
        if (uid <= 0 || !this.ShouldTryV2(type) || !await this.ServerCarriesV2Async(type))
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

        var bounds = await this.ServerBoundsAsync(type);

        if (!TrackingV2Translator.TryTranslate(type, body, bounds, out var v2Body, out var unsupported))
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

    public async Task DeleteByUidAsync(string type, string userId, int uid)
    {
        if (await this.TryDeleteByUidV2Async(type, userId, uid))
        {
            return;
        }

        var request = this.CreateRequest(HttpMethod.Delete, $"{this._apiAddress}/api/tracking/{type}/{Encode(userId)}/byUid/{uid}?silent=true");
        var response = await this._httpClient.SendAsync(request);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            LogDeleteNotFound(this._logger, type, uid);
            return;
        }

        EnsureAccountStillExists(response);
        response.EnsureSuccessStatusCode();
    }

    public async Task BulkDeleteByUidsAsync(string type, string userId, IEnumerable<int> uids)
    {
        var uidList = uids.ToList();
        if (uidList.Count == 0)
        {
            return;
        }

        if (await this.TryBulkDeleteByUidsV2Async(type, userId, uidList))
        {
            return;
        }

        var request = this.CreateRequest(HttpMethod.Post, $"{this._apiAddress}/api/tracking/{type}/{Encode(userId)}/delete?silent=true");
        request.Content = new StringContent(
            JsonSerializer.Serialize(uidList.Select(u => (long)u)),
            Encoding.UTF8,
            "application/json");

        var response = await this._httpClient.SendAsync(request);
        EnsureAccountStillExists(response);
        response.EnsureSuccessStatusCode();
    }

    public async Task<JsonElement> GetAllTrackingAsync(string userId)
    {
        var request = this.CreateRequest(HttpMethod.Get, $"{this._apiAddress}/api/tracking/all/{Encode(userId)}");
        var response = await this._httpClient.SendAsync(request);
        EnsureAccountStillExists(response);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    public async Task<JsonElement> GetAllTrackingAllProfilesAsync(string userId)
    {
        var request = this.CreateRequest(HttpMethod.Get, $"{this._apiAddress}/api/tracking/allProfiles/{Encode(userId)}?includeDescriptions=true");
        var response = await this._httpClient.SendAsync(request);
        EnsureAccountStillExists(response);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    public async Task ReloadStateAsync()
    {
        var request = this.CreateRequest(HttpMethod.Get, $"{this._apiAddress}/api/reload");
        var response = await this._httpClient.SendAsync(request);
        EnsureAccountStillExists(response);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// The numeric limits THIS server declares for its own v2 rules, read from the <c>/openapi.json</c> it
    /// publishes and cached for ten minutes.
    /// </summary>
    /// <remarks>
    /// Read from the server rather than shipped as a table because the two disagree. PoracleNG bounds all
    /// 55 numeric filter fields on an unreleased branch, and this site stores values outside ten of them --
    /// values PoracleNG itself wrote. Applying those limits to a server that does not enforce them was
    /// measured at 68% of one instance's Pokemon rules dropping off the v2 write path, for no benefit,
    /// because that server accepts every one of the values. A 5.2.1 publishes exactly one bound.
    /// <para>
    /// Unreachable or unparseable yields no bounds rather than all bounds, so a failed fetch leaves the
    /// write exactly as it is today and lets v2 answer for itself.
    /// </para>
    /// </remarks>
    private async Task<IReadOnlyDictionary<string, TrackingV2Translator.Bound>?> ServerBoundsAsync(string type)
    {
        if (!this._cache.TryGetValue<IReadOnlyDictionary<string, IReadOnlyDictionary<string, TrackingV2Translator.Bound>>>(
                V2BoundsCacheKey, out var byType)
            || byType is null)
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

            byType = V2SchemaBounds.Parse(document);
            this._cache.Set(V2BoundsCacheKey, byType, V2BoundsFor);
        }

        return byType.TryGetValue(type, out var bounds) ? bounds : null;
    }

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
    /// Invasion is the one type with a v2 surface that PoracleWeb deliberately stays off. A v2 read of a
    /// named-grunt rule carries no targeting field at all, and PoracleWeb holds only the grunt name, which
    /// live data fills with values it cannot reverse into an id (<c>blanche</c>, <c>npc 0</c>, <c>player
    /// team leader</c>). Filed upstream.
    /// </remarks>
    private bool ShouldTryV2(string type) =>
        TrackingV2Translator.Handles(type) && this._trackingApiVersion != "v1";

    /// <summary>Whether a delete may go to v2 at all.</summary>
    /// <remarks>
    /// Deliberately does not consult <see cref="TrackingV2Translator"/> the way <see cref="ShouldTryV2"/>
    /// does. A delete carries no rule body, so there is nothing to translate and nothing v2 could fail to
    /// express -- which puts invasion, the one type held off the v2 writes, in scope here.
    /// </remarks>
    private bool ShouldTryV2Delete() => this._trackingApiVersion != "v1";

    /// <summary>
    /// Whether the server is believed to carry v2. Pinned to <c>v2</c> this skips the probe but not the
    /// runtime fallback, so pinning a server that turns out not to have the route degrades to v1 rather
    /// than failing every edit.
    /// </summary>
    private async Task<bool> ServerCarriesV2Async(string type)
    {
        if (this._cache.TryGetValue(V2AbsentCacheKey(type), out _))
        {
            return false;
        }

        if (this._trackingApiVersion == "v2")
        {
            return true;
        }

        var profile = await this._serverProfile.GetAsync();
        return profile.SupportsV2Tracking;
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
    /// Deletes one rule through <c>/api/v2</c>. Returns false when the caller should use v1 instead --
    /// either because the route is not there, or because v2 refused a rule v1 would have deleted.
    /// </summary>
    private async Task<bool> TryDeleteByUidV2Async(string type, string userId, int uid)
    {
        if (uid <= 0 || !this.ShouldTryV2Delete() || !await this.ServerCarriesV2Async(type))
        {
            return false;
        }

        var request = this.CreateRequest(
            HttpMethod.Delete,
            $"{this._apiAddress}/api/v2/humans/{Encode(userId)}/tracking/{type}/{uid}?silent=true");

        var response = await this._httpClient.SendAsync(request);

        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        var payload = await response.Content.ReadAsStringAsync();

        switch (response.StatusCode)
        {
            case HttpStatusCode.NotFound when !PoracleProblemDetails.IsProblemJson(payload):
                // gin's plaintext "404 page not found": the route is absent on this build whatever
                // /health claimed. Same probe the write path uses.
                this._cache.Set(V2AbsentCacheKey(type), true, V2AbsentFor);
                LogV2RouteAbsent(this._logger, type);
                return false;

            case HttpStatusCode.NotFound when payload.Contains("human not found", StringComparison.Ordinal):
                throw new AccountGoneException();

            case HttpStatusCode.NotFound:
                // The rule exists but not on the active profile. v1 deletes by (human, uid) whatever
                // profile the row sits on, and a uid on screen can outlive a profile switch made by
                // PoracleNG's active-hours scheduler -- so this goes to v1 rather than being reported
                // as already gone, which would blank the card and leave the row. See #860.
                LogV2DeleteNotOnActiveProfile(this._logger, type, uid);
                return false;

            default:
                response.EnsureSuccessStatusCode();
                return false;
        }
    }

    /// <summary>
    /// Deletes several rules through <c>/api/v2</c>. Returns false when the caller should use v1 instead,
    /// which includes v2 having taken only some of them.
    /// </summary>
    /// <remarks>
    /// v2 skips a uid outside the active profile silently and answers 200, so the count in
    /// <c>{deleted}</c> is the only signal that it did less than was asked. Anything short sends the whole
    /// set to v1: deleting an already-deleted uid there is a no-op, so repeating the ones v2 did take
    /// costs a round trip and changes nothing.
    /// </remarks>
    private async Task<bool> TryBulkDeleteByUidsV2Async(string type, string userId, List<int> uids)
    {
        if (!this.ShouldTryV2Delete() || uids.Exists(u => u <= 0) || !await this.ServerCarriesV2Async(type))
        {
            return false;
        }

        var uidList = string.Join(',', uids);
        if (uidList.Length > V2BulkDeleteMaxUidQueryLength)
        {
            return false;
        }

        var request = this.CreateRequest(
            HttpMethod.Delete,
            $"{this._apiAddress}/api/v2/humans/{Encode(userId)}/tracking/{type}?uid={uidList}&silent=true");

        var response = await this._httpClient.SendAsync(request);
        var payload = await response.Content.ReadAsStringAsync();

        if (response.IsSuccessStatusCode)
        {
            return CountV2Deleted(payload) >= uids.Count;
        }

        switch (response.StatusCode)
        {
            case HttpStatusCode.NotFound when !PoracleProblemDetails.IsProblemJson(payload):
                this._cache.Set(V2AbsentCacheKey(type), true, V2AbsentFor);
                LogV2RouteAbsent(this._logger, type);
                return false;

            case HttpStatusCode.NotFound when payload.Contains("human not found", StringComparison.Ordinal):
                throw new AccountGoneException();

            case HttpStatusCode.NotFound:
                return false;

            default:
                response.EnsureSuccessStatusCode();
                return false;
        }
    }

    /// <summary>
    /// How many rules v2 reports it removed. -1 when the answer cannot be read, which the caller treats
    /// the same as "fewer than asked" -- an unreadable 200 is not evidence that the delete happened.
    /// </summary>
    private static int CountV2Deleted(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);

            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("deleted", out var deleted)
                && deleted.ValueKind == JsonValueKind.Array
                    ? deleted.GetArrayLength()
                    : -1;
        }
        catch (JsonException)
        {
            return -1;
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

    [LoggerMessage(Level = LogLevel.Debug, Message = "Delete {Type} uid={Uid} returned 404 (already deleted)")]
    private static partial void LogDeleteNotFound(ILogger logger, string type, int uid);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "v2 will not delete {Type} uid={Uid}: not on the active profile. Using v1, which deletes it regardless.")]
    private static partial void LogV2DeleteNotOnActiveProfile(ILogger logger, string type, int uid);

    [LoggerMessage(Level = LogLevel.Information, Message = "Create {Type} for {UserId} request: {Body}")]
    private static partial void LogCreateRequest(ILogger logger, string type, string userId, string body);

    [LoggerMessage(Level = LogLevel.Information, Message = "Create {Type} for {UserId} response: {Response}")]
    private static partial void LogCreateResponse(ILogger logger, string type, string userId, string response);
}
