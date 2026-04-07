using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Pgan.PoracleWebNet.Core.Abstractions.Services;

namespace Pgan.PoracleWebNet.Core.Services;

public partial class GolbatApiProxy(HttpClient httpClient, IConfiguration configuration, ILogger<GolbatApiProxy> logger) : IGolbatApiProxy
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly string _apiAddress = (configuration["Golbat:ApiAddress"] ?? string.Empty).TrimEnd('/');
    private readonly string _apiSecret = configuration["Golbat:ApiSecret"] ?? string.Empty;
    private readonly ILogger<GolbatApiProxy> _logger = logger;

    public async Task<IReadOnlyList<int>> GetAvailablePokemonAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var request = this.CreateRequest(HttpMethod.Get, $"{this._apiAddress}/api/pokemon/available");
            var response = await this._httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            return ParsePokemonIds(json);
        }
        catch (Exception ex)
        {
            LogGetAvailablePokemonFailed(this._logger, ex);
            return [];
        }
    }

    private static List<int> ParsePokemonIds(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Handle flat array: [1,2,3]
        if (root.ValueKind == JsonValueKind.Array)
        {
            return ExtractIds(root);
        }

        // Handle wrapped object: {"available": [1,2,3]}
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("available", out var available) && available.ValueKind == JsonValueKind.Array)
        {
            return ExtractIds(available);
        }

        return [];
    }

    private static List<int> ExtractIds(JsonElement array)
    {
        var ids = new HashSet<int>();
        foreach (var element in array.EnumerateArray())
        {
            // Handle flat integer array: [1, 2, 3]
            if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var flatId))
            {
                ids.Add(flatId);
            }
            // Handle object array: [{"id": 1, "form": 0, "count": 100}, ...]
            else if (element.ValueKind == JsonValueKind.Object &&
                     element.TryGetProperty("id", out var idProp) &&
                     idProp.TryGetInt32(out var objId))
            {
                ids.Add(objId);
            }
        }
        return [.. ids];
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        if (!string.IsNullOrEmpty(this._apiSecret))
        {
            request.Headers.Add("X-Golbat-Secret", this._apiSecret);
        }
        return request;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to fetch available Pokemon from Golbat API")]
    private static partial void LogGetAvailablePokemonFailed(ILogger logger, Exception ex);
}
