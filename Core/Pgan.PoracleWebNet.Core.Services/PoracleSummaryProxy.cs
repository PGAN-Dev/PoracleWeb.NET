using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

public class PoracleSummaryProxy(HttpClient httpClient, IConfiguration configuration) : IPoracleSummaryProxy
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly string _apiAddress = configuration["Poracle:ApiAddress"] ?? string.Empty;
    private readonly string _apiSecret = configuration["Poracle:ApiSecret"] ?? string.Empty;

    /// <summary>
    /// URL-encodes a path segment for safe path construction. User IDs can be full webhook URLs
    /// containing slashes that would break routing; alert types are server-validated but encoded
    /// too for defense-in-depth consistency.
    /// </summary>
    private static string Encode(string segment) => Uri.EscapeDataString(segment);

    public async Task<JsonElement?> GetSchedulesAsync(string userId)
    {
        var response = await this.SendAsync(HttpMethod.Get, $"/api/summaries/{Encode(userId)}");
        if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
        {
            throw new SummaryBackendUnavailableException();
        }

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        // PoracleNG wraps the response: { "status": "ok", "schedules": [ ... ] }
        return doc.RootElement.TryGetProperty("schedules", out var schedules) ? schedules.Clone() : doc.RootElement.Clone();
    }

    public async Task<JsonElement?> GetScheduleAsync(string userId, string alertType)
    {
        var response = await this.SendAsync(HttpMethod.Get, $"/api/summaries/{Encode(userId)}/{Encode(alertType)}");
        if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
        {
            throw new SummaryBackendUnavailableException();
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        // PoracleNG wraps the response: { "status": "ok", "schedule": { ... } }
        return doc.RootElement.TryGetProperty("schedule", out var schedule) ? schedule.Clone() : doc.RootElement.Clone();
    }

    public async Task SetScheduleAsync(string userId, string alertType, string activeHoursJson)
    {
        var body = $"{{\"active_hours\":{(string.IsNullOrWhiteSpace(activeHoursJson) ? "[]" : activeHoursJson)}}}";
        var response = await this.SendAsync(HttpMethod.Post, $"/api/summaries/{Encode(userId)}/{Encode(alertType)}", body);
        if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
        {
            throw new SummaryBackendUnavailableException();
        }

        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteScheduleAsync(string userId, string alertType)
    {
        var response = await this.SendAsync(HttpMethod.Delete, $"/api/summaries/{Encode(userId)}/{Encode(alertType)}");
        if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
        {
            throw new SummaryBackendUnavailableException();
        }

        response.EnsureSuccessStatusCode();
    }

    public async Task TriggerAsync(string userId, string alertType)
    {
        var response = await this.SendAsync(HttpMethod.Post, $"/api/summaries/{Encode(userId)}/{Encode(alertType)}/trigger");
        if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
        {
            throw new SummaryBackendUnavailableException();
        }

        response.EnsureSuccessStatusCode();
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
