using Pgan.PoracleWebNet.Core.Models;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Pgan.PoracleWebNet.Core.Abstractions.Services;

namespace Pgan.PoracleWebNet.Core.Services;

public class PoracleHumanProxy(HttpClient httpClient, IConfiguration configuration) : IPoracleHumanProxy
{
    /// <summary>What to say when PoracleNG refused and explained nothing usable.</summary>
    private const string Unexplained = "Poracle rejected the request.";

    private readonly HttpClient _httpClient = httpClient;
    private readonly string _apiAddress = configuration["Poracle:ApiAddress"] ?? string.Empty;
    private readonly string _apiSecret = configuration["Poracle:ApiSecret"] ?? string.Empty;

    /// <summary>
    /// URL-encodes a userId for safe path construction. Webhook IDs are full URLs
    /// containing slashes that would break routing without encoding.
    /// </summary>
    private static string Encode(string userId) => Uri.EscapeDataString(userId);

    /// <summary>
    /// Turns a refusal from PoracleNG into the answer it deserves, and lets everything else throw.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only 404 was read here, and one 409 on the delete-place path. Everything else fell through to
    /// <c>EnsureSuccessStatusCode()</c>, whose <see cref="HttpRequestException"/> the global handler
    /// flattens into 500 "An unexpected error occurred" -- so "state is required (true/false)",
    /// "profile_no must be specified" and "invalid latitude" all reached the user as a server fault and
    /// were logged as one. #539 fixed exactly this on the tracking proxy and never reached here.
    /// </para>
    /// <para>
    /// 422 is the same refusal wearing a different number. Verified live: the v1 human and profile routes
    /// answer 400 on 5.1.0 and 5.2.1 alike, but 5.2.1's <c>/api/v2/humans</c> surface answers RFC 9457
    /// problem+json at 422 for the same mistakes. Matching only 400 would re-open this the day a call
    /// site moves to v2.
    /// </para>
    /// <para>
    /// A 404 is account-gone only when PoracleNG says so. It also answers 404 "Profile not found" when a
    /// profile number does not exist, and gin answers a plaintext "404 page not found" for a route this
    /// build does not have -- treating either as a dead account signed the user out of a working session,
    /// and in <c>ProfileOverviewService</c>'s restore-the-profile <c>finally</c> it did so while
    /// swallowing the real failure. Both verified against 5.1.0 and 5.2.1.
    /// </para>
    /// </remarks>
    private static async Task EnsureAcceptedAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var payload = await response.Content.ReadAsStringAsync();

        switch (response.StatusCode)
        {
            case HttpStatusCode.NotFound when NamesAMissingAccount(payload):
                // A JWT outlives the account it names. Without this, every lookup for a deleted user threw
                // an HttpRequestException that the global handler flattened into a 500, so the SPA -- which
                // signs out only on 401 -- left the user in an app where every page failed. See #584.
                throw new AccountGoneException();

            case HttpStatusCode.NotFound:
                throw new PoracleRequestRefusedException(
                    PoracleProblemDetails.Describe(payload, Unexplained), (int)HttpStatusCode.NotFound);

            case HttpStatusCode.BadRequest:
            case HttpStatusCode.UnprocessableEntity:
                throw new PoracleRequestRefusedException(PoracleProblemDetails.Describe(payload, Unexplained));

            default:
                response.EnsureSuccessStatusCode();
                return;
        }
    }

    /// <summary>
    /// True when a 404 body is PoracleNG saying the account itself is gone rather than something in it.
    /// </summary>
    /// <remarks>
    /// v1 answers <c>{"message":"User not found"}</c>; the v2 surface and the tracking routes say
    /// "human not found". Anything else at 404 names a profile, a place or a route.
    /// </remarks>
    private static bool NamesAMissingAccount(string? payload) =>
        payload is not null
        && (payload.Contains("user not found", StringComparison.OrdinalIgnoreCase)
            || payload.Contains("human not found", StringComparison.OrdinalIgnoreCase));

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
        await EnsureAcceptedAsync(response);
    }

    public async Task StartAsync(string userId)
    {
        var response = await this.SendAsync(HttpMethod.Post, $"/api/humans/{Encode(userId)}/start");
        await EnsureAcceptedAsync(response);
    }

    public async Task StopAsync(string userId)
    {
        var response = await this.SendAsync(HttpMethod.Post, $"/api/humans/{Encode(userId)}/stop");
        await EnsureAcceptedAsync(response);
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
        await EnsureAcceptedAsync(response);
    }

    public async Task SetLocationAsync(string userId, double lat, double lon)
    {
        var response = await this.SendAsync(HttpMethod.Post, $"/api/humans/{Encode(userId)}/setLocation/{lat}/{lon}");
        await EnsureAcceptedAsync(response);
    }

    public async Task SetAreasAsync(string userId, string[] areas)
    {
        var body = JsonSerializer.Serialize(areas);
        var response = await this.SendAsync(HttpMethod.Post, $"/api/humans/{Encode(userId)}/setAreas", body);
        await EnsureAcceptedAsync(response);
    }

    public async Task<JsonElement?> GetAreasAsync(string userId) =>
        // User's selected areas are in GET /api/humans/one/{id} → human.area (JSON string).
        // GET /api/humans/{id} returns the available area list, not the user's selection.
        await this.GetHumanAsync(userId);

    public async Task SwitchProfileAsync(string userId, int profileNo)
    {
        var response = await this.SendAsync(HttpMethod.Post, $"/api/humans/{Encode(userId)}/switchProfile/{profileNo}");
        await EnsureAcceptedAsync(response);
    }

    public async Task<JsonElement> GetProfilesAsync(string userId)
    {
        var response = await this.SendAsync(HttpMethod.Get, $"/api/profiles/{Encode(userId)}");
        await EnsureAcceptedAsync(response);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    public async Task AddProfileAsync(string userId, JsonElement body)
    {
        var response = await this.SendAsync(HttpMethod.Post, $"/api/profiles/{Encode(userId)}/add", body.GetRawText());
        await EnsureAcceptedAsync(response);
    }

    public async Task UpdateProfileAsync(string userId, JsonElement body)
    {
        var response = await this.SendAsync(HttpMethod.Post, $"/api/profiles/{Encode(userId)}/update", body.GetRawText());
        await EnsureAcceptedAsync(response);
    }

    public async Task DeleteProfileAsync(string userId, int profileNo)
    {
        var response = await this.SendAsync(HttpMethod.Delete, $"/api/profiles/{Encode(userId)}/byProfileNo/{profileNo}");
        await EnsureAcceptedAsync(response);
    }

    public async Task CopyProfileAsync(string userId, int fromProfileNo, int toProfileNo)
    {
        var response = await this.SendAsync(HttpMethod.Post, $"/api/profiles/{Encode(userId)}/copy/{fromProfileNo}/{toProfileNo}");
        await EnsureAcceptedAsync(response);
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
        await EnsureAcceptedAsync(response);

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
        await EnsureAcceptedAsync(response);

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

        // Read before the general refusal path: a 409 here names the alarms still pointing at the place,
        // which is the difference between "could not delete" and knowing what to repoint first.
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

        await EnsureAcceptedAsync(response);
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
