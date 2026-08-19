using Pgan.PoracleWebNet.Core.Models;
using System.Net;
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
    /// <summary>
    /// Turns PoracleNG's "user not found" into something the API can answer 401 to.
    /// </summary>
    /// <remarks>
    /// A JWT outlives the account it names. Without this, every lookup for a deleted user threw an
    /// HttpRequestException that the global handler flattened into a 500, so the SPA -- which signs out
    /// only on 401 -- left the user in an app where every page failed. See #584.
    /// </remarks>
    private static void EnsureAccountStillExists(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new AccountGoneException();
        }
    }

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
        EnsureAccountStillExists(response);
        response.EnsureSuccessStatusCode();
    }

    public async Task StartAsync(string userId)
    {
        var response = await this.SendAsync(HttpMethod.Post, $"/api/humans/{Encode(userId)}/start");
        EnsureAccountStillExists(response);
        response.EnsureSuccessStatusCode();
    }

    public async Task StopAsync(string userId)
    {
        var response = await this.SendAsync(HttpMethod.Post, $"/api/humans/{Encode(userId)}/stop");
        EnsureAccountStillExists(response);
        response.EnsureSuccessStatusCode();
    }

    public async Task AdminDisabledAsync(string userId, bool disabled)
    {
        // PoracleNG's adminDisabledRequest is `State *bool \`json:"state"\`` -- it rejects any other key
        // with 400 "state is required (true/false)", including the `adminDisable` this used to send, so
        // ban/unban failed on every call against every PoracleNG.
        var body = JsonSerializer.Serialize(new
        {
            state = disabled
        });
        var response = await this.SendAsync(HttpMethod.Post, $"/api/humans/{Encode(userId)}/adminDisabled", body);
        EnsureAccountStillExists(response);
        response.EnsureSuccessStatusCode();
    }

    public async Task SetLocationAsync(string userId, double lat, double lon)
    {
        var response = await this.SendAsync(HttpMethod.Post, $"/api/humans/{Encode(userId)}/setLocation/{lat}/{lon}");
        EnsureAccountStillExists(response);
        response.EnsureSuccessStatusCode();
    }

    public async Task SetAreasAsync(string userId, string[] areas)
    {
        var body = JsonSerializer.Serialize(areas);
        var response = await this.SendAsync(HttpMethod.Post, $"/api/humans/{Encode(userId)}/setAreas", body);
        EnsureAccountStillExists(response);
        response.EnsureSuccessStatusCode();
    }

    public async Task<JsonElement?> GetAreasAsync(string userId) =>
        // User's selected areas are in GET /api/humans/one/{id} → human.area (JSON string).
        // GET /api/humans/{id} returns the available area list, not the user's selection.
        await this.GetHumanAsync(userId);

    public async Task SwitchProfileAsync(string userId, int profileNo)
    {
        var response = await this.SendAsync(HttpMethod.Post, $"/api/humans/{Encode(userId)}/switchProfile/{profileNo}");
        EnsureAccountStillExists(response);
        response.EnsureSuccessStatusCode();
    }

    public async Task<JsonElement> GetProfilesAsync(string userId)
    {
        var response = await this.SendAsync(HttpMethod.Get, $"/api/profiles/{Encode(userId)}");
        EnsureAccountStillExists(response);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    public async Task AddProfileAsync(string userId, JsonElement body)
    {
        var response = await this.SendAsync(HttpMethod.Post, $"/api/profiles/{Encode(userId)}/add", body.GetRawText());
        EnsureAccountStillExists(response);
        response.EnsureSuccessStatusCode();
    }

    public async Task UpdateProfileAsync(string userId, JsonElement body)
    {
        var response = await this.SendAsync(HttpMethod.Post, $"/api/profiles/{Encode(userId)}/update", body.GetRawText());
        EnsureAccountStillExists(response);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteProfileAsync(string userId, int profileNo)
    {
        var response = await this.SendAsync(HttpMethod.Delete, $"/api/profiles/{Encode(userId)}/byProfileNo/{profileNo}");
        EnsureAccountStillExists(response);
        response.EnsureSuccessStatusCode();
    }

    public async Task CopyProfileAsync(string userId, int fromProfileNo, int toProfileNo)
    {
        var response = await this.SendAsync(HttpMethod.Post, $"/api/profiles/{Encode(userId)}/copy/{fromProfileNo}/{toProfileNo}");
        EnsureAccountStillExists(response);
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


    public async Task<SavedPlaces> GetPlacesAsync(string userId)
    {
        var response = await this.SendAsync(HttpMethod.Get, $"/api/humans/{Encode(userId)}/locations");
        EnsureAccountStillExists(response);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        // PoracleNG wraps this one as {"locations": {...}, "status": "ok"} -- reading the root as the
        // payload returns an empty set rather than an error, which is the whole reason this note exists.
        if (!doc.RootElement.TryGetProperty("locations", out var locations))
        {
            return new SavedPlaces();
        }

        var result = new SavedPlaces();

        if (locations.TryGetProperty("default", out var def) && def.ValueKind == JsonValueKind.Object)
        {
            result.Default = new SavedPlace
            {
                Label = string.Empty,
                Latitude = def.GetDoubleProp("latitude"),
                Longitude = def.GetDoubleProp("longitude"),
            };
        }

        if (locations.TryGetProperty("named", out var named) && named.ValueKind == JsonValueKind.Array)
        {
            foreach (var place in named.EnumerateArray())
            {
                result.Named.Add(new SavedPlace
                {
                    Label = place.GetStringProp("label"),
                    Latitude = place.GetDoubleProp("latitude"),
                    Longitude = place.GetDoubleProp("longitude"),
                });
            }
        }

        return result;
    }

    public async Task<string?> AddPlaceAsync(string userId, SavedPlace place)
    {
        var body = JsonSerializer.Serialize(new
        {
            label = place.Label,
            latitude = place.Latitude,
            longitude = place.Longitude,
        });

        var response = await this.SendAsync(
            HttpMethod.Post, $"/api/humans/{Encode(userId)}/locations/add", body);
        EnsureAccountStillExists(response);
        response.EnsureSuccessStatusCode();

        // A rejected label is reported inside a 200: PoracleNG answers per row so a batch can partly
        // succeed. Treating the 200 as success stored nothing and told the user it worked.
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("results", out var results)
            || results.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var row in results.EnumerateArray())
        {
            var error = row.GetStringPropOrNull("error");
            if (!string.IsNullOrEmpty(error))
            {
                return error;
            }
        }

        return null;
    }

    public async Task DeletePlaceAsync(string userId, string label)
    {
        var response = await this.SendAsync(
            HttpMethod.Post, $"/api/humans/{Encode(userId)}/locations/{Encode(label)}/delete");
        EnsureAccountStillExists(response);

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            var conflict = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(conflict);
            var rules = doc.RootElement.TryGetProperty("referencing_rules", out var refs)
                && refs.ValueKind == JsonValueKind.Array
                    ? refs.EnumerateArray().Select(r => r.ToString()).ToList()
                    : [];

            throw new PlaceInUseException(rules);
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
