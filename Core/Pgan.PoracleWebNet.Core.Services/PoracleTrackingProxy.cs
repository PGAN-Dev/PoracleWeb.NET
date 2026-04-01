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
    private readonly HttpClient _httpClient = httpClient;
    private readonly string _apiAddress = configuration["Poracle:ApiAddress"] ?? string.Empty;
    private readonly string _apiSecret = configuration["Poracle:ApiSecret"] ?? string.Empty;
    private readonly ILogger<PoracleTrackingProxy> _logger = logger;

    public async Task<JsonElement> GetByUserAsync(string type, string userId)
    {
        var request = this.CreateRequest(HttpMethod.Get, $"{this._apiAddress}/api/tracking/{type}/{userId}");
        var response = await this._httpClient.SendAsync(request);
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
        var request = this.CreateRequest(HttpMethod.Post, $"{this._apiAddress}/api/tracking/{type}/{userId}?silent=true");
        request.Content = new StringContent(bodyText, Encoding.UTF8, "application/json");

        var response = await this._httpClient.SendAsync(request);
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
        var request = this.CreateRequest(HttpMethod.Delete, $"{this._apiAddress}/api/tracking/{type}/{userId}/byUid/{uid}");
        var response = await this._httpClient.SendAsync(request);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            LogDeleteNotFound(this._logger, type, uid);
            return;
        }

        response.EnsureSuccessStatusCode();
    }

    public async Task BulkDeleteByUidsAsync(string type, string userId, IEnumerable<int> uids)
    {
        var uidList = uids.ToList();
        if (uidList.Count == 0)
        {
            return;
        }

        var request = this.CreateRequest(HttpMethod.Post, $"{this._apiAddress}/api/tracking/{type}/{userId}/delete");
        request.Content = new StringContent(
            JsonSerializer.Serialize(uidList.Select(u => (long)u)),
            Encoding.UTF8,
            "application/json");

        var response = await this._httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    public async Task<JsonElement> GetAllTrackingAsync(string userId)
    {
        var request = this.CreateRequest(HttpMethod.Get, $"{this._apiAddress}/api/tracking/all/{userId}");
        var response = await this._httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    public async Task ReloadStateAsync()
    {
        var request = this.CreateRequest(HttpMethod.Get, $"{this._apiAddress}/api/reload");
        var response = await this._httpClient.SendAsync(request);
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

    [LoggerMessage(Level = LogLevel.Debug, Message = "Delete {Type} uid={Uid} returned 404 (already deleted)")]
    private static partial void LogDeleteNotFound(ILogger logger, string type, int uid);

    [LoggerMessage(Level = LogLevel.Information, Message = "Create {Type} for {UserId} request: {Body}")]
    private static partial void LogCreateRequest(ILogger logger, string type, string userId, string body);

    [LoggerMessage(Level = LogLevel.Information, Message = "Create {Type} for {UserId} response: {Response}")]
    private static partial void LogCreateResponse(ILogger logger, string type, string userId, string response);
}
