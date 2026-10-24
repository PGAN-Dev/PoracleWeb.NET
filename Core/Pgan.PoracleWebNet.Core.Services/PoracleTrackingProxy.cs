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
    IUserAreaPolicy areaPolicy,
    IMemoryCache cache,
    ILogger<PoracleTrackingProxy> logger) : IPoracleTrackingProxy
{
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

    private readonly IUserAreaPolicy _areaPolicy = areaPolicy;
    private readonly IMemoryCache _cache = cache;
    private readonly ILogger<PoracleTrackingProxy> _logger = logger;

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

        // Invasion and incident keep separate uid sequences, so the same number can name a grunt rule and an
        // unrelated event. The retry as incident happens only for a uid the stored rules show is an event.
        JsonElement? snapshot = null;
        if (type == InvasionType)
        {
            snapshot = await this.GetAllTrackingAllProfilesAsync(userId);
            if (EventOnlyUids(snapshot.Value, [uid]).Contains(uid)
                && await this.DeleteOneV2Async(IncidentType, userId, uid, profileNo: null))
            {
                return;
            }
        }

        if ((await this.DeleteOnOtherProfilesAsync(type, userId, [uid], singleRoute: true, snapshot)).Count > 0)
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

        JsonElement? snapshot = null;
        if (type == InvasionType && remaining.Count > 0)
        {
            // Only uids the stored rules show are events go to incident; see DeleteByUidAsync.
            snapshot = await this.GetAllTrackingAllProfilesAsync(userId);
            var events = EventOnlyUids(snapshot.Value, remaining);
            if (events.Count > 0)
            {
                remaining = remaining.Except(await this.DeleteManyV2Async(IncidentType, userId, events, profileNo: null)).ToList();
            }
        }

        if (remaining.Count > 0)
        {
            remaining = await this.DeleteOnOtherProfilesAsync(type, userId, remaining, singleRoute: false, snapshot);
        }

        if (remaining.Count > 0)
        {
            LogBulkDeleteLeftUids(this._logger, type, LogSafe.Id(userId), new UidList(remaining));
        }
    }

    /// <summary>
    /// Deletes the uids the active profile did not hold from whichever of the human's profiles holds them,
    /// with <c>?profile=N</c>. One all-profiles read places them; an invasion uid whose row is a Pokestop
    /// event (the read folds incident into invasion) is deleted as an incident.
    /// </summary>
    /// <param name="singleRoute">Re-issue on the single-rule route, as the caller's own delete was.</param>
    /// <returns>The uids no profile holds.</returns>
    private async Task<List<int>> DeleteOnOtherProfilesAsync(
        string type, string userId, List<int> uids, bool singleRoute, JsonElement? snapshot = null)
    {
        var all = snapshot ?? await this.GetAllTrackingAllProfilesAsync(userId);
        var snapshotKey = type == IncidentType ? InvasionType : type;
        var wanted = uids.ToHashSet();
        var placed = new Dictionary<(string Type, int Profile), List<int>>();

        if (all.ValueKind == JsonValueKind.Object
            && all.TryGetProperty(snapshotKey, out var rows)
            && rows.ValueKind == JsonValueKind.Array)
        {
            var plainUids = snapshotKey == InvasionType
                ? rows.EnumerateArray().Where(r => !IsEventRow(r)).Select(UidOf).ToHashSet()
                : [];

            foreach (var row in rows.EnumerateArray())
            {
                var uid = UidOf(row);
                var isEvent = snapshotKey == InvasionType && IsEventRow(row);

                // The snapshot folds events into invasion, so a plain grunt rule sharing an incident
                // uid is not the rule the caller named. The reverse holds too: an invasion delete whose
                // uid names a grunt rule never takes an event that shares the number. The kind is judged
                // before the uid is claimed, so a row of the wrong kind cannot consume it.
                if ((type == IncidentType && !isEvent) || (type == InvasionType && isEvent && plainUids.Contains(uid)))
                {
                    continue;
                }

                if (!wanted.Remove(uid)
                    || !row.TryGetProperty("profile_no", out var no)
                    || no.ValueKind != JsonValueKind.Number)
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

    /// <summary>Whether a row of the folded invasion read is a Pokestop event, by the marker the read uses.</summary>
    private static bool IsEventRow(JsonElement row) =>
        row.TryGetProperty("grunt_type", out var grunt)
        && grunt.ValueKind == JsonValueKind.String
        && PokestopEventTypes.IsEventName(grunt.GetString());

    /// <summary>The uids whose every row in the folded invasion read is an event, and at least one is.</summary>
    private static List<int> EventOnlyUids(JsonElement all, IEnumerable<int> uids)
    {
        if (all.ValueKind != JsonValueKind.Object
            || !all.TryGetProperty(InvasionType, out var rows)
            || rows.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var events = new HashSet<int>();
        var plain = new HashSet<int>();
        foreach (var row in rows.EnumerateArray())
        {
            (IsEventRow(row) ? events : plain).Add(UidOf(row));
        }

        return uids.Where(u => events.Contains(u) && !plain.Contains(u)).ToList();
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
        Message = "Delete {Type} uid={Uid}: no profile of this human holds that rule (all were searched); treating it as already gone.")]
    private static partial void LogDeleteAlreadyGone(ILogger logger, string type, int uid);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Bulk delete {Type} for {UserId}: PoracleNG did not report deleting uids {Uids} (already gone, or not on the active profile).")]
    private static partial void LogBulkDeleteLeftUids(ILogger logger, string type, string userId, UidList uids);
}
