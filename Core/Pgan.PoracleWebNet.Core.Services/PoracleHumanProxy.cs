using Pgan.PoracleWebNet.Core.Models;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Pgan.PoracleWebNet.Core.Abstractions.Services;

namespace Pgan.PoracleWebNet.Core.Services;

public partial class PoracleHumanProxy(
    HttpClient httpClient,
    IConfiguration configuration,
    IPoracleServerProfileService serverProfile,
    IMemoryCache cache,
    ILogger<PoracleHumanProxy> logger) : IPoracleHumanProxy
{
    /// <summary>What to say when PoracleNG refused and explained nothing usable.</summary>
    private const string Unexplained = "Poracle rejected the request.";

    /// <summary>The release that first carries <c>/api/v2/humans</c>.</summary>
    private static readonly Version FirstWithV2 = new(5, 2, 0);

    /// <summary>
    /// How long a route is remembered as missing. Matches the server profile's own cache, so an upgrade
    /// is picked up on the same clock as every other version-gated thing.
    /// </summary>
    private static readonly TimeSpan V2AbsentFor = TimeSpan.FromMinutes(5);

    private readonly HttpClient _httpClient = httpClient;
    private readonly string _apiAddress = configuration["Poracle:ApiAddress"] ?? string.Empty;
    private readonly string _apiSecret = configuration["Poracle:ApiSecret"] ?? string.Empty;
    private readonly IPoracleServerProfileService _serverProfile = serverProfile;
    private readonly IMemoryCache _cache = cache;
    private readonly ILogger<PoracleHumanProxy> _logger = logger;

    /// <summary>
    /// URL-encodes a userId for safe path construction. Webhook IDs are full URLs
    /// containing slashes that would break routing without encoding.
    /// </summary>
    private static string Encode(string userId) => Uri.EscapeDataString(userId);

    /// <summary>A latitude or longitude in a form PoracleNG parses whatever the server's culture is.</summary>
    /// <remarks>
    /// The v1 location paths interpolate the doubles straight into the URL. On a machine whose current
    /// culture uses a comma for the decimal separator that produced <c>/setLocation/51,5/-0,12</c>, which
    /// is four path segments rather than two. The v2 body does not have the problem -- <c>JsonSerializer</c>
    /// is invariant -- but the v1 fallback is still there and still has to be right.
    /// </remarks>
    private static string Coord(double value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Whether PoracleNG is believed to carry <c>/api/v2</c>, from the version it reports.
    /// </summary>
    /// <remarks>
    /// A belief, not a fact: a fork can carry the routes while reporting an older number, or the reverse.
    /// <see cref="TryV2Async"/> therefore also handles the route being absent at request time, so being
    /// wrong here costs one extra round-trip rather than the operation.
    /// </remarks>
    private async Task<bool> ServerCarriesV2Async()
    {
        var profile = await this._serverProfile.GetAsync();
        return profile.Reachable && profile.ParsedVersion is { } version && version >= FirstWithV2;
    }

    /// <summary>
    /// Sends one request to <c>/api/v2</c>, or answers null when this server has no such route so the
    /// caller can use its v1 path for the same request instead of failing it.
    /// </summary>
    /// <remarks>
    /// The absence is remembered per route, not per surface. One shared flag would let a single missing
    /// route drop every other call back to v1 -- and <c>PUT /locations/{label}</c> has no v1 path at all,
    /// so for that one "fall back" means "vanish".
    /// </remarks>
    private async Task<(HttpResponseMessage Response, string Payload)?> TryV2Async(
        HttpMethod method, string route, string path, string? body = null)
    {
        var absentKey = $"poracle:v2-humans-absent:{route}";
        if (this._cache.TryGetValue(absentKey, out _))
        {
            return null;
        }

        if (!await this.ServerCarriesV2Async())
        {
            return null;
        }

        var reply = await this.SendReadAsync(method, path, body);

        // gin answers a route it does not have with the plaintext "404 page not found"; the v2 surface
        // answers a missing human or place with problem+json at the same status. Verified against 5.1.0
        // and 5.2.1 -- the content type is the only thing separating them.
        if (reply.Response.StatusCode == HttpStatusCode.NotFound
            && !PoracleProblemDetails.IsProblemJson(reply.Payload))
        {
            this._cache.Set(absentKey, true, V2AbsentFor);
            LogV2RouteAbsent(this._logger, route);
            return null;
        }

        return reply;
    }

    private async Task<(HttpResponseMessage Response, string Payload)> SendReadAsync(
        HttpMethod method, string path, string? body = null)
    {
        var response = await this.SendAsync(method, path, body);
        return (response, await response.Content.ReadAsStringAsync());
    }

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

            case HttpStatusCode.Conflict:
                // v2 answers 409 where v1 buried the same refusal in a 200 body -- a duplicate saved-place
                // label is the live case. Without this it fell through to EnsureSuccessStatusCode and the
                // global handler turned "you already have one called that" into a 500.
                throw new PoracleRequestRefusedException(
                    PoracleProblemDetails.Describe(payload, Unexplained), (int)HttpStatusCode.Conflict);

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
        // Both surfaces answer the same wrapper and the same columns -- verified field by field against a
        // live 5.2.1 -- so nothing downstream can tell which one answered.
        var (response, json) =
            await this.TryV2Async(HttpMethod.Get, "get", $"/api/v2/humans/{Encode(userId)}")
            ?? await this.SendReadAsync(HttpMethod.Get, $"/api/humans/one/{Encode(userId)}");

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

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
        var (response, _) =
            await this.TryV2Async(HttpMethod.Post, "enable", $"/api/v2/humans/{Encode(userId)}/enable")
            ?? await this.SendReadAsync(HttpMethod.Post, $"/api/humans/{Encode(userId)}/start");

        await EnsureAcceptedAsync(response);
    }

    public async Task StopAsync(string userId)
    {
        var (response, _) =
            await this.TryV2Async(HttpMethod.Post, "disable", $"/api/v2/humans/{Encode(userId)}/disable")
            ?? await this.SendReadAsync(HttpMethod.Post, $"/api/humans/{Encode(userId)}/stop");

        await EnsureAcceptedAsync(response);
    }

    /// <inheritdoc />
    public async Task SetLanguageAsync(string userId, string language)
    {
        var v2Body = JsonSerializer.Serialize(new
        {
            language
        });

        var (response, _) =
            await this.TryV2Async(
                HttpMethod.Post, "language", $"/api/v2/humans/{Encode(userId)}/language", v2Body)
            ?? await this.SendReadAsync(
                HttpMethod.Post, $"/api/humans/{Encode(userId)}/language", v2Body);

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

        // v2 renamed the key as well as the route: its adminDisableBody is `Disabled *bool`, so sending
        // v1's `state` to it is a 422 rather than a no-op.
        var v2Body = JsonSerializer.Serialize(new
        {
            disabled
        });

        var (response, _) =
            await this.TryV2Async(
                HttpMethod.Post, "admin-disable", $"/api/v2/humans/{Encode(userId)}/admin-disable", v2Body)
            ?? await this.SendReadAsync(
                HttpMethod.Post, $"/api/humans/{Encode(userId)}/adminDisabled", body);

        await EnsureAcceptedAsync(response);
    }

    public async Task SetLocationAsync(string userId, double lat, double lon)
    {
        var v2Body = JsonSerializer.Serialize(new
        {
            lat,
            lon
        });

        var (response, _) =
            await this.TryV2Async(
                HttpMethod.Post, "location", $"/api/v2/humans/{Encode(userId)}/location", v2Body)
            ?? await this.SendReadAsync(
                HttpMethod.Post,
                $"/api/humans/{Encode(userId)}/setLocation/{Coord(lat)}/{Coord(lon)}");

        await EnsureAcceptedAsync(response);
    }

    /// <inheritdoc />
    public async Task<string?> GetAdminRolesAsync(string userId)
    {
        var (response, payload) =
            await this.TryV2Async(HttpMethod.Get, "admin-roles", $"/api/v2/humans/{Encode(userId)}/admin-roles")
            ?? await this.SendReadAsync(
                HttpMethod.Get, $"/api/humans/{Encode(userId)}/getAdministrationRoles");

        // A 404 is PoracleNG answering: this human has no roles because it has no such human. Anything
        // else non-2xx is PoracleNG failing to answer, and returning null for it -- which is what this
        // did for every status -- told UserRoleResolver "no delegated webhooks" confidently enough to
        // cache for a minute. That is #656 and #667 on the one source their fix did not cover.
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return payload;
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
        var v2Body = JsonSerializer.Serialize(new
        {
            profile_no = profileNo
        });

        var (response, _) =
            await this.TryV2Async(
                HttpMethod.Post, "profile", $"/api/v2/humans/{Encode(userId)}/profile", v2Body)
            ?? await this.SendReadAsync(
                HttpMethod.Post, $"/api/humans/{Encode(userId)}/switchProfile/{profileNo}");

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


    public async Task<SavedPlaces> GetPlacesAsync(string userId)
    {
        var (response, json) =
            await this.TryV2Async(HttpMethod.Get, "locations", $"/api/v2/humans/{Encode(userId)}/locations")
            ?? await this.SendReadAsync(HttpMethod.Get, $"/api/humans/{Encode(userId)}/locations");

        await EnsureAcceptedAsync(response);

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
        // Ours, not PoracleNG's. v1 reported the overflow as "Data too long for column 'label'" inside a
        // 200 and v2 answers 500 {"detail":"database error"} -- both verified live, and neither is
        // something to show a person who typed a long name. humans_locations.label is varchar(64).
        if (place.Label is { Length: > 64 })
        {
            return "That name is too long. Use 64 characters or fewer.";
        }

        // v2 takes lat/lon on the way in and still answers latitude/longitude on the way out. The
        // asymmetry is upstream's, not a typo here.
        var v2Body = JsonSerializer.Serialize(new
        {
            label = place.Label,
            lat = place.Latitude,
            lon = place.Longitude,
        });

        var v2 = await this.TryV2Async(
            HttpMethod.Post, "locations-add", $"/api/v2/humans/{Encode(userId)}/locations", v2Body);

        if (v2 is { } reply)
        {
            // v2 turns the duplicate label into a real 409 instead of burying it in a 200. Returned as
            // the same string v1 produced so the controller and the SPA see one behaviour.
            if (reply.Response.StatusCode == HttpStatusCode.Conflict)
            {
                return PoracleProblemDetails.Describe(reply.Payload, Unexplained);
            }

            await EnsureAcceptedAsync(reply.Response);
            return null;
        }

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

    /// <inheritdoc />
    public async Task<bool> UpdatePlaceAsync(string userId, string label, double latitude, double longitude)
    {
        var body = JsonSerializer.Serialize(new
        {
            lat = latitude,
            lon = longitude,
        });

        var v2 = await this.TryV2Async(
            HttpMethod.Put, "locations-update", $"/api/v2/humans/{Encode(userId)}/locations/{Encode(label)}", body);

        if (v2 is not { } reply)
        {
            return false;
        }

        await EnsureAcceptedAsync(reply.Response);
        return true;
    }

    public async Task DeletePlaceAsync(string userId, string label)
    {
        var reply =
            await this.TryV2Async(
                HttpMethod.Delete, "locations-delete", $"/api/v2/humans/{Encode(userId)}/locations/{Encode(label)}")
            ?? await this.SendReadAsync(
                HttpMethod.Post, $"/api/humans/{Encode(userId)}/locations/{Encode(label)}/delete");

        // Read before the general refusal path: a 409 here names the alarms still pointing at the place,
        // which is the difference between "could not delete" and knowing what to repoint first.
        if (reply.Response.StatusCode == HttpStatusCode.Conflict)
        {
            throw new PlaceInUseException(ReferencingRules(reply.Payload));
        }

        await EnsureAcceptedAsync(reply.Response);
    }

    /// <summary>
    /// The alarms blocking a place delete, as "raid 424" rather than a fragment of JSON.
    /// </summary>
    /// <remarks>
    /// Both versions answer 409 with a <c>referencing_rules</c> array, and they disagree about case:
    /// v2 tags its fields (<c>{"type":"raid","uid":424}</c>) while v1 serialises
    /// <c>store.ReferencingRule</c>, which carries no json tags at all, so Go's default marshalling
    /// emits <c>{"Type":"raid","UID":424}</c>. Reading both keeps the message intact whichever path
    /// answered.
    ///
    /// This used to call <c>ToString()</c> on each element, which put the raw JSON object into the
    /// list. Nothing broke, because the only consumer counts the entries -- but the component's own
    /// spec mocks readable strings the server had never produced.
    /// </remarks>
    private static List<string> ReferencingRules(string payload)
    {
        var rules = new List<string>();

        try
        {
            using var doc = JsonDocument.Parse(payload);

            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("referencing_rules", out var refs)
                || refs.ValueKind != JsonValueKind.Array)
            {
                return rules;
            }

            foreach (var entry in refs.EnumerateArray())
            {
                rules.Add(Describe(entry));
            }
        }
        catch (JsonException)
        {
            // A refusal we cannot read still refuses; the caller reports the count it has.
        }

        return rules;
    }

    private static string Describe(JsonElement entry)
    {
        if (entry.ValueKind == JsonValueKind.String)
        {
            return entry.GetString() ?? string.Empty;
        }

        if (entry.ValueKind != JsonValueKind.Object)
        {
            return entry.ToString();
        }

        var type = ReadString(entry, "type") ?? ReadString(entry, "Type");
        var uid = ReadString(entry, "uid") ?? ReadString(entry, "UID");

        return type is null && uid is null
            ? entry.ToString()
            : string.Join(' ', new[] { type, uid }.Where(v => !string.IsNullOrWhiteSpace(v)));
    }

    private static string? ReadString(JsonElement entry, string name)
        => entry.TryGetProperty(name, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.ToString(),
                _ => null,
            }
            : null;

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

    [LoggerMessage(
        EventId = 6301,
        Level = LogLevel.Debug,
        Message = "PoracleNG has no /api/v2 route for {Route}; using the v1 path for it.")]
    private static partial void LogV2RouteAbsent(ILogger logger, string route);
}
