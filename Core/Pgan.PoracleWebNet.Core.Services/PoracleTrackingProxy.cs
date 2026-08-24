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
    IMemoryCache cache,
    ILogger<PoracleTrackingProxy> logger) : IPoracleTrackingProxy
{
    /// <summary>
    /// The only type this build writes through <c>/api/v2</c>. See #805 — the other nine stay on the
    /// frozen v1 surface, which 5.2.1 left unchanged, so leaving them is a no-op rather than a deferred
    /// defect. Each needs its own field translation derived from its own schema.
    /// </summary>
    private const string V2PilotType = "pokemon";

    /// <summary>
    /// Set when the v2 route answered gin's plaintext 404, meaning this server does not carry it whatever
    /// its version said. Held for as long as the server profile is cached, so an upgrade is picked up on
    /// the same clock as everything else version-gated.
    /// </summary>
    private const string V2AbsentCacheKey = "poracle:v2-tracking-absent";

    private static readonly TimeSpan V2AbsentFor = TimeSpan.FromMinutes(5);

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
        if (response.StatusCode == HttpStatusCode.BadRequest)
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
    public async Task<TrackingUpdateResult> UpdateByUidAsync(
        string type, string userId, int uid, JsonElement body)
    {
        if (uid > 0 && this.ShouldTryV2(type) && await this.ServerCarriesV2Async())
        {
            if (TrackingV2Translator.TryTranslatePokemon(body, out var v2Body, out var unsupported))
            {
                var applied = await this.PutV2Async(type, userId, uid, v2Body);
                if (applied is { } result)
                {
                    return result;
                }
            }
            else
            {
                // Not a fault. The row carries something v2 has no faithful place for, so it goes to v1,
                // which stores whatever it is given. See TrackingV2Translator.
                LogV2Untranslatable(this._logger, type, uid, unsupported ?? "unknown");
            }
        }

        // v1: an update is a create carrying the uid, which PoracleNG upserts. Byte-identical to what
        // PoracleWeb has always sent, which is what makes 5.1.0 a genuine no-change.
        var created = await this.CreateAsync(type, userId, body);
        return new TrackingUpdateResult(created.PrimaryUid ?? uid, false);
    }

    public async Task DeleteByUidAsync(string type, string userId, int uid)
    {
        var request = this.CreateRequest(HttpMethod.Delete, $"{this._apiAddress}/api/tracking/{type}/{Encode(userId)}/byUid/{uid}");
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

        var request = this.CreateRequest(HttpMethod.Post, $"{this._apiAddress}/api/tracking/{type}/{Encode(userId)}/delete");
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

    /// <summary>Whether this type and this deployment are in scope for the v2 write path at all.</summary>
    private bool ShouldTryV2(string type) =>
        string.Equals(type, V2PilotType, StringComparison.Ordinal)
        && this._trackingApiVersion != "v1";

    /// <summary>
    /// Whether the server is believed to carry v2. Pinned to <c>v2</c> this skips the probe but not the
    /// runtime fallback, so pinning a server that turns out not to have the route degrades to v1 rather
    /// than failing every edit.
    /// </summary>
    private async Task<bool> ServerCarriesV2Async()
    {
        if (this._cache.TryGetValue(V2AbsentCacheKey, out _))
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
                this._cache.Set(V2AbsentCacheKey, true, V2AbsentFor);
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

    /// <summary>Reads whatever explanation PoracleNG returned, falling back to something honest.</summary>
    private static async Task<string> ExtractMessageAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();

        try
        {
            var root = JsonDocument.Parse(body).RootElement;
            foreach (var name in new[] { "message", "error", "status" })
            {
                if (root.TryGetProperty(name, out var value)
                    && value.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(value.GetString()))
                {
                    return value.GetString()!;
                }
            }
        }
        catch (JsonException)
        {
            // Not JSON; the raw body is still better than nothing, as long as it is short.
        }

        return string.IsNullOrWhiteSpace(body) || body.Length > 300
            ? "Poracle rejected the alarm."
            : body;
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
        Level = LogLevel.Warning,
        Message = "PoracleNG has no /api/v2 {Type} route despite reporting a version that should carry it. Using v1.")]
    private static partial void LogV2RouteAbsent(ILogger logger, string type);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Delete {Type} uid={Uid} returned 404 (already deleted)")]
    private static partial void LogDeleteNotFound(ILogger logger, string type, int uid);

    [LoggerMessage(Level = LogLevel.Information, Message = "Create {Type} for {UserId} request: {Body}")]
    private static partial void LogCreateRequest(ILogger logger, string type, string userId, string body);

    [LoggerMessage(Level = LogLevel.Information, Message = "Create {Type} for {UserId} response: {Response}")]
    private static partial void LogCreateResponse(ILogger logger, string type, string userId, string response);
}
