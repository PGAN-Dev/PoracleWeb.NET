using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

/// <summary>
/// Talks to PoracleNG's v2 mute endpoints. Modelled on <see cref="PoracleSummaryProxy"/> -- same
/// <c>X-Poracle-Secret</c> header and the same path-segment encoding, because a human id can be a full
/// webhook URL.
/// </summary>
/// <remarks>
/// Everything upstream returns is wrapped: <c>{mutes:[...]}</c>, <c>{mute:{...},replaced:bool}</c> and
/// <c>{deleted:[...]}</c>. Forgetting to unwrap a PoracleNG response is a documented repeat failure here,
/// so each shape is unwrapped explicitly rather than by a shared guess.
/// </remarks>
public class PoracleMuteProxy(HttpClient httpClient, IConfiguration configuration) : IPoracleMuteProxy
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly string _apiAddress = configuration["Poracle:ApiAddress"] ?? string.Empty;
    private readonly string _apiSecret = configuration["Poracle:ApiSecret"] ?? string.Empty;

    private static string Encode(string segment) => Uri.EscapeDataString(segment);

    private static string MutePath(string userId) => $"/api/v2/humans/{Encode(userId)}/mutes";

    public async Task<IReadOnlyList<Mute>> ListAsync(string userId, CancellationToken cancellationToken = default)
    {
        using var response = await this.SendAsync(HttpMethod.Get, MutePath(userId), null, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return [];
        }

        return await ReadListAsync(response, "mutes", cancellationToken);
    }

    public async Task<(Mute Mute, bool Replaced)> CreateAsync(
        string userId, string scope, string? value, int durationMinutes, CancellationToken cancellationToken = default)
    {
        var payload = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["scope"] = scope,
            ["duration_min"] = durationMinutes,
        };

        // 'everything' takes no value, and upstream 422s on an empty string as readily as on a present one.
        if (!string.IsNullOrEmpty(value))
        {
            payload["value"] = value;
        }

        using var response = await this.SendAsync(
            HttpMethod.Post, MutePath(userId), JsonSerializer.Serialize(payload), cancellationToken);

        await ThrowIfRejectedAsync(response, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var mute = root.TryGetProperty("mute", out var muteElement) ? ParseMute(muteElement) : new Mute();
        var replaced = root.TryGetProperty("replaced", out var replacedElement)
            && replacedElement.ValueKind == JsonValueKind.True;

        return (mute, replaced);
    }

    public async Task<IReadOnlyList<Mute>> DeleteAsync(
        string userId, string scope, string? value, CancellationToken cancellationToken = default)
    {
        var query = $"?scope={Uri.EscapeDataString(scope)}";
        if (!string.IsNullOrEmpty(value))
        {
            query += $"&value={Uri.EscapeDataString(value)}";
        }

        using var response = await this.SendAsync(HttpMethod.Delete, MutePath(userId) + query, null, cancellationToken);

        // Already lapsed. The store is in memory and expires entries on its own, so "not found" is the
        // ordinary outcome of resuming something a moment after its countdown ran out -- not an error.
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return [];
        }

        await ThrowIfRejectedAsync(response, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await ReadListAsync(response, "deleted", cancellationToken);
    }

    public async Task<IReadOnlyList<Mute>> DeleteAllAsync(string userId, CancellationToken cancellationToken = default)
    {
        using var response = await this.SendAsync(HttpMethod.Delete, MutePath(userId), null, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return [];
        }

        response.EnsureSuccessStatusCode();

        return await ReadListAsync(response, "deleted", cancellationToken);
    }

    private static async Task<IReadOnlyList<Mute>> ReadListAsync(
        HttpResponseMessage response, string property, CancellationToken cancellationToken)
    {
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty(property, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var mutes = new List<Mute>();
        foreach (var element in array.EnumerateArray())
        {
            mutes.Add(ParseMute(element));
        }

        return mutes;
    }

    private static Mute ParseMute(JsonElement element) => new()
    {
        Scope = element.TryGetProperty("scope", out var scope) && scope.ValueKind == JsonValueKind.String
            ? scope.GetString() ?? string.Empty
            : string.Empty,
        Value = element.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null,
        ExpiresAt = element.TryGetProperty("expires_at", out var expires) && expires.ValueKind == JsonValueKind.Number
            ? expires.GetInt64()
            : 0,
        RemainingSecs = element.TryGetProperty("remaining_secs", out var remaining) && remaining.ValueKind == JsonValueKind.Number
            ? remaining.GetInt64()
            : 0,
    };

    /// <summary>
    /// Turns huma's 422 envelope into a message a user can act on. The readable sentence is
    /// <c>detail</c>; schema violations put the specific complaint in <c>errors[].message</c> and leave
    /// detail as the useless "validation failed", so the first error wins when one is present.
    /// </summary>
    private static async Task ThrowIfRejectedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode != HttpStatusCode.UnprocessableEntity)
        {
            return;
        }

        var reason = "Poracle refused that.";

        try
        {
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.TryGetProperty("errors", out var errors)
                && errors.ValueKind == JsonValueKind.Array
                && errors.GetArrayLength() > 0
                && errors[0].TryGetProperty("message", out var message)
                && message.ValueKind == JsonValueKind.String)
            {
                reason = message.GetString() ?? reason;
            }
            else if (root.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String)
            {
                reason = detail.GetString() ?? reason;
            }
        }
        catch (JsonException)
        {
            // Keep the generic reason: a 422 whose body is not huma's envelope is still a refusal.
        }

        throw new MuteRejectedException(reason);
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string path, string? body, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(method, $"{this._apiAddress.TrimEnd('/')}{path}");
        if (!string.IsNullOrEmpty(this._apiSecret))
        {
            request.Headers.Add("X-Poracle-Secret", this._apiSecret);
        }

        if (body != null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        return await this._httpClient.SendAsync(request, cancellationToken);
    }
}
