using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Pgan.PoracleWebNet.Core.Abstractions.Services;

namespace Pgan.PoracleWebNet.Core.Services;

public class PoracleHumanProxy(HttpClient httpClient, IConfiguration configuration) : IPoracleHumanProxy
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly string _apiAddress = configuration["Poracle:ApiAddress"] ?? string.Empty;
    private readonly string _apiSecret = configuration["Poracle:ApiSecret"] ?? string.Empty;

    /// <summary>
    /// URL-encodes a userId for safe path construction. Webhook IDs are full URLs
    /// containing slashes that would break routing without encoding.
    /// </summary>
    private static string Encode(string userId) => Uri.EscapeDataString(userId);

    public async Task<JsonElement?> GetHumanAsync(string userId)
    {
        var response = await this.SendAsync(HttpMethod.Get, $"/api/humans/one/{Encode(userId)}");
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        // PoracleNG wraps the response: { "human": { ... }, "status": "ok" }
        if (doc.RootElement.TryGetProperty("human", out var human))
        {
            return human.Clone();
        }

        return doc.RootElement.Clone();
    }

    public async Task CreateHumanAsync(JsonElement body)
    {
        var response = await this.SendAsync(HttpMethod.Post, "/api/humans", body.GetRawText());
        response.EnsureSuccessStatusCode();
    }

    public async Task StartAsync(string userId)
    {
        var response = await this.SendAsync(HttpMethod.Post, $"/api/humans/{Encode(userId)}/start");
        response.EnsureSuccessStatusCode();
    }

    public async Task StopAsync(string userId)
    {
        var response = await this.SendAsync(HttpMethod.Post, $"/api/humans/{Encode(userId)}/stop");
        response.EnsureSuccessStatusCode();
    }

    public async Task AdminDisabledAsync(string userId, bool disabled)
    {
        var body = JsonSerializer.Serialize(new { adminDisable = disabled ? 1 : 0 });
        var response = await this.SendAsync(HttpMethod.Post, $"/api/humans/{Encode(userId)}/adminDisabled", body);
        response.EnsureSuccessStatusCode();
    }

    public async Task SetLocationAsync(string userId, double lat, double lon)
    {
        var response = await this.SendAsync(HttpMethod.Post, $"/api/humans/{Encode(userId)}/setLocation/{lat}/{lon}");
        response.EnsureSuccessStatusCode();
    }

    public async Task SetAreasAsync(string userId, string[] areas)
    {
        var body = JsonSerializer.Serialize(areas);
        var response = await this.SendAsync(HttpMethod.Post, $"/api/humans/{Encode(userId)}/setAreas", body);
        response.EnsureSuccessStatusCode();
    }

    public async Task<JsonElement?> GetAreasAsync(string userId)
    {
        // User's selected areas are in GET /api/humans/one/{id} → human.area (JSON string).
        // GET /api/humans/{id} returns the available area list, not the user's selection.
        return await this.GetHumanAsync(userId);
    }

    public async Task SwitchProfileAsync(string userId, int profileNo)
    {
        var response = await this.SendAsync(HttpMethod.Post, $"/api/humans/{Encode(userId)}/switchProfile/{profileNo}");
        response.EnsureSuccessStatusCode();
    }

    public async Task<JsonElement> GetProfilesAsync(string userId)
    {
        var response = await this.SendAsync(HttpMethod.Get, $"/api/profiles/{Encode(userId)}");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    public async Task AddProfileAsync(string userId, JsonElement body)
    {
        var response = await this.SendAsync(HttpMethod.Post, $"/api/profiles/{Encode(userId)}/add", body.GetRawText());
        response.EnsureSuccessStatusCode();
    }

    public async Task UpdateProfileAsync(string userId, JsonElement body)
    {
        var response = await this.SendAsync(HttpMethod.Post, $"/api/profiles/{Encode(userId)}/update", body.GetRawText());
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteProfileAsync(string userId, int profileNo)
    {
        var response = await this.SendAsync(HttpMethod.Delete, $"/api/profiles/{Encode(userId)}/byProfileNo/{profileNo}");
        response.EnsureSuccessStatusCode();
    }

    public async Task<JsonElement?> CheckLocationAsync(string userId, double lat, double lon)
    {
        var response = await this.SendAsync(HttpMethod.Get, $"/api/humans/{Encode(userId)}/checkLocation/{lat}/{lon}");
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? body = null)
    {
        var request = new HttpRequestMessage(method, $"{this._apiAddress}{path}");
        if (!string.IsNullOrEmpty(this._apiSecret))
        {
            request.Headers.Add("X-Poracle-Secret", this._apiSecret);
        }

        if (body != null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        return await this._httpClient.SendAsync(request);
    }
}
