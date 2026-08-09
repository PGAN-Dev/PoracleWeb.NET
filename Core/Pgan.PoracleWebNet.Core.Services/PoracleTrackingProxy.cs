using Pgan.PoracleWebNet.Core.Models;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Pgan.PoracleWebNet.Core.Abstractions.Services;

namespace Pgan.PoracleWebNet.Core.Services;

public partial class PoracleTrackingProxy(
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<PoracleTrackingProxy> logger) : IPoracleTrackingProxy
{
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

    [LoggerMessage(Level = LogLevel.Debug, Message = "Delete {Type} uid={Uid} returned 404 (already deleted)")]
    private static partial void LogDeleteNotFound(ILogger logger, string type, int uid);

    [LoggerMessage(Level = LogLevel.Information, Message = "Create {Type} for {UserId} request: {Body}")]
    private static partial void LogCreateRequest(ILogger logger, string type, string userId, string body);

    [LoggerMessage(Level = LogLevel.Information, Message = "Create {Type} for {UserId} response: {Response}")]
    private static partial void LogCreateResponse(ILogger logger, string type, string userId, string response);
}
