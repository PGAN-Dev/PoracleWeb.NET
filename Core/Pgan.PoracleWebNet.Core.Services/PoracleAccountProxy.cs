using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

/// <summary>
/// Talks to PoracleNG's v2 linked-accounts endpoints (<c>/api/v2/humans/{id}/accounts</c>), which hold the
/// game accounts personal shiny alerts are matched against.
/// </summary>
/// <remarks>
/// <para>
/// Same <c>X-Poracle-Secret</c> header and path-segment encoding as <see cref="PoracleMuteProxy"/>, because
/// a human id can be a full webhook URL. Errors are mapped the way <c>PoracleHumanProxy</c> maps them: v2 is
/// huma-generated, so a refusal is problem+json and its <c>detail</c> is the sentence a user can act on.
/// </para>
/// <para>
/// A player id is case-sensitive. It goes into the path and the body exactly as given -- never lowercased
/// or trimmed here.
/// </para>
/// </remarks>
public class PoracleAccountProxy(HttpClient httpClient, IConfiguration configuration) : IPoracleAccountProxy
{
    private const string Unexplained = "Poracle refused that account change.";

    private readonly HttpClient _httpClient = httpClient;
    private readonly string _apiAddress = configuration["Poracle:ApiAddress"] ?? string.Empty;
    private readonly string _apiSecret = configuration["Poracle:ApiSecret"] ?? string.Empty;

    private static string Encode(string segment) => Uri.EscapeDataString(segment);

    private static string AccountsPath(string userId) => $"/api/v2/humans/{Encode(userId)}/accounts";

    private static string AccountPath(string userId, string playerId) => $"{AccountsPath(userId)}/{Encode(playerId)}";

    public async Task<IReadOnlyList<PlayerAccount>> ListAsync(string userId, CancellationToken ct = default)
    {
        using var response = await this.SendAsync(HttpMethod.Get, AccountsPath(userId), null, ct);
        await EnsureAcceptedAsync(response, ct);

        var json = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("accounts", out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return [.. array.EnumerateArray().Select(ParseAccount)];
    }

    public async Task<PlayerAccount> AddAsync(string userId, string playerId, string? label, CancellationToken ct = default)
    {
        var payload = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["player_id"] = playerId,
        };

        // label is omitempty upstream; a blank one means "no label", not an empty one to store.
        if (!string.IsNullOrWhiteSpace(label))
        {
            payload["label"] = label;
        }

        using var response = await this.SendAsync(HttpMethod.Post, AccountsPath(userId), JsonSerializer.Serialize(payload), ct);
        await EnsureAcceptedAsync(response, ct);

        var json = await response.Content.ReadAsStringAsync(ct);
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("account", out var account) || account.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("PoracleNG answered an account add without the account.");
        }

        return ParseAccount(account);
    }

    public async Task RelabelAsync(string userId, string playerId, string label, CancellationToken ct = default)
    {
        // An empty label is sent as an empty string: upstream reads that as "clear it".
        var body = JsonSerializer.Serialize(new Dictionary<string, string>(StringComparer.Ordinal) { ["label"] = label ?? string.Empty });

        using var response = await this.SendAsync(HttpMethod.Put, AccountPath(userId, playerId), body, ct);
        await EnsureAcceptedAsync(response, ct);
    }

    public async Task DeleteAsync(string userId, string playerId, CancellationToken ct = default)
    {
        using var response = await this.SendAsync(HttpMethod.Delete, AccountPath(userId, playerId), null, ct);
        await EnsureAcceptedAsync(response, ct);
    }

    private static PlayerAccount ParseAccount(JsonElement element) => new(
        element.TryGetProperty("player_id", out var id) && id.ValueKind == JsonValueKind.String
            ? id.GetString() ?? string.Empty
            : string.Empty,
        element.TryGetProperty("label", out var label) && label.ValueKind == JsonValueKind.String
            ? label.GetString()
            : null,
        element.TryGetProperty("created_at", out var created) && created.ValueKind == JsonValueKind.String
            && created.TryGetDateTimeOffset(out var createdAt)
            ? createdAt
            : default);

    /// <summary>
    /// Maps a refusal to something the API can answer. A 404 whose body is not problem+json is gin saying
    /// the route does not exist (a PoracleNG without linked accounts); a problem+json 404 is huma naming a
    /// missing human or account.
    /// </summary>
    private static async Task EnsureAcceptedAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var payload = await response.Content.ReadAsStringAsync(ct);

        switch (response.StatusCode)
        {
            case HttpStatusCode.NotFound when !PoracleProblemDetails.IsProblemJson(payload):
                throw new PoracleRequestRefusedException(
                    "This Poracle server does not support game accounts.", (int)HttpStatusCode.NotImplemented);

            case HttpStatusCode.NotFound:
                throw new PoracleRequestRefusedException(
                    PoracleProblemDetails.Describe(payload, Unexplained), (int)HttpStatusCode.NotFound);

            case HttpStatusCode.Conflict:
                throw new PoracleRequestRefusedException(
                    PoracleProblemDetails.Describe(payload, Unexplained), (int)HttpStatusCode.Conflict);

            case HttpStatusCode.BadRequest:
            case HttpStatusCode.UnprocessableEntity:
                throw new PoracleRequestRefusedException(
                    PoracleProblemDetails.Describe(payload, Unexplained), (int)HttpStatusCode.UnprocessableEntity);

            default:
                response.EnsureSuccessStatusCode();
                return;
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, $"{this._apiAddress.TrimEnd('/')}{path}");
        if (!string.IsNullOrEmpty(this._apiSecret))
        {
            request.Headers.Add("X-Poracle-Secret", this._apiSecret);
        }

        if (body != null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        return await this._httpClient.SendAsync(request, ct);
    }
}
