using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Models.Helpers;
using System.Globalization;
using System.Linq;
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
    IPoracleV2SchemaService v2Schema,
    IAreaSecurityPolicyService areaSecurityPolicy,
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
    private readonly IPoracleV2SchemaService _v2Schema = v2Schema;
    private readonly IAreaSecurityPolicyService _areaSecurityPolicy = areaSecurityPolicy;
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

    /// <summary>
    /// Whether <c>setAreas</c>'s <c>trusted</c> flag is safe to rely on for the community-restriction
    /// case right now -- the schema has to declare the property AND area_security has to be confirmed
    /// off, because neither <c>/openapi.json</c> nor <c>/health</c> can tell a pre-jfberry/PoracleNG#230
    /// server from a post-#230 one (verified live: byte-identical either side of the fix). See #838.
    /// </summary>
    private async Task<bool> CanUseTrustedSetAreasAsync()
    {
        var capabilities = await this._v2Schema.GetAsync();
        return capabilities.TrustedSetAreas && await this._areaSecurityPolicy.IsConfirmedDisabledAsync();
    }

    /// <summary>The active profile's current area list (<c>humans.area</c>), or null if unreadable.</summary>
    private async Task<List<string>?> GetCurrentAreaListAsync(string userId)
    {
        var human = await this.GetHumanAsync(userId);
        var areaJson = human?.GetStringPropOrNull("area");
        return areaJson is null ? null : AreaListJson.Parse(areaJson);
    }

    /// <summary>
    /// Posts a full area-list replacement with <c>trusted: true</c>. <c>setAreas</c> replaces the whole
    /// list -- there is no incremental add/remove on the wire -- so every caller here reads the current
    /// list first and posts the complete result.
    /// </summary>
    private async Task PostTrustedAreasAsync(string userId, List<string> areas)
    {
        var body = JsonSerializer.Serialize(new { areas, trusted = true });
        var (response, payload) =
            await this.SendReadAsync(HttpMethod.Post, $"/api/v2/humans/{Encode(userId)}/areas", body);
        await EnsureAcceptedAsync(response);

        using var doc = JsonDocument.Parse(payload);
        if (doc.RootElement.TryGetProperty("rejected", out var rejectedEl)
            && rejectedEl.ValueKind == JsonValueKind.Array
            && rejectedEl.GetArrayLength() > 0)
        {
            var rejected = rejectedEl.EnumerateArray().Select(e => e.GetString() ?? string.Empty);

            // Should not happen for a name this app just created or already had selected -- trusted
            // only lifts userSelectable, not "exists at all". Logged rather than surfaced: every caller
            // here already treats its own write as best-effort (see the HACK: trusted-set-areas sites
            // this replaces), and the geofence row these names came from is the source of truth either way.
            LogTrustedAreasRejected(this._logger, userId, string.Join(", ", rejected));
        }
    }

    /// <summary>
    /// Adds one area to the human's active profile via <c>setAreas</c>'s <c>trusted</c> flag, which
    /// bypasses the <c>userSelectable</c> filter a user-drawn geofence always fails for a non-admin
    /// caller. See #838.
    /// </summary>
    /// <returns>
    /// <c>null</c> when <see cref="CanUseTrustedSetAreasAsync"/> says no or the current list could not
    /// be read -- the caller must fall back to <c>IUserAreaDualWriter</c>; <c>false</c> when the area
    /// was already present; <c>true</c> once written.
    /// </returns>
    public async Task<bool?> AddAreaToActiveProfileTrustedAsync(string userId, string areaName) =>
        await this.AddAreasToActiveProfileTrustedAsync(userId, [areaName]);

    /// <summary>Bulk form of <see cref="AddAreaToActiveProfileTrustedAsync"/>.</summary>
    public async Task<bool?> AddAreasToActiveProfileTrustedAsync(string userId, IReadOnlyCollection<string> areaNames)
    {
        if (!await this.CanUseTrustedSetAreasAsync())
        {
            return null;
        }

        var normalized = areaNames
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Select(a => a.ToLowerInvariant())
            .Distinct()
            .ToList();

        if (normalized.Count == 0)
        {
            return false;
        }

        var current = await this.GetCurrentAreaListAsync(userId);
        if (current is null)
        {
            return null;
        }

        var currentSet = new HashSet<string>(current, StringComparer.OrdinalIgnoreCase);
        var changed = false;
        foreach (var name in normalized)
        {
            if (currentSet.Add(name))
            {
                current.Add(name);
                changed = true;
            }
        }

        if (!changed)
        {
            return false;
        }

        await this.PostTrustedAreasAsync(userId, current);
        return true;
    }

    /// <summary>Removes one area from the human's active profile via the same trusted call.</summary>
    public async Task<bool?> RemoveAreaFromActiveProfileTrustedAsync(string userId, string areaName)
    {
        if (!await this.CanUseTrustedSetAreasAsync())
        {
            return null;
        }

        var current = await this.GetCurrentAreaListAsync(userId);
        if (current is null)
        {
            return null;
        }

        var removed = current.RemoveAll(a => string.Equals(a, areaName, StringComparison.OrdinalIgnoreCase)) > 0;
        if (!removed)
        {
            return false;
        }

        await this.PostTrustedAreasAsync(userId, current);
        return true;
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "setAreas(trusted) rejected areas for {UserId} that this app expected to be accepted: {Rejected}")]
    private static partial void LogTrustedAreasRejected(ILogger logger, string userId, string rejected);

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

    /// <inheritdoc />
    public async Task<int?> AddProfileAsync(string userId, JsonElement body)
    {
        // The v2 route exists on 5.2.1 too, and there it answers {"status":"ok"} with no number. So the
        // route being present is not the question -- whether its 200 carries profile_no is, and only the
        // schema says that. Without the check this would read a number out of a body that has none and
        // quietly answer null, which is the same as today but a round trip slower. See #836.
        if ((await this._v2Schema.GetAsync()).ProfileCreateReturnsNumber
            && V2ProfileBody(body, isUpdate: false) is { } v2Body
            && await this.TryV2Async(
                HttpMethod.Post, "profiles-add", $"/api/v2/humans/{Encode(userId)}/profiles", v2Body)
                is { } reply)
        {
            await EnsureAcceptedAsync(reply.Response);

            return ProfileNoFrom(reply.Payload);
        }

        var response = await this.SendAsync(HttpMethod.Post, $"/api/profiles/{Encode(userId)}/add", body.GetRawText());
        await EnsureAcceptedAsync(response);

        return null;
    }

    /// <inheritdoc />
    public async Task<bool> UpdateProfileAsync(string userId, JsonElement body)
    {
        // Gated, and the gate is load-bearing rather than an optimisation. PATCH exists on 5.2.1, where
        // V2UpdateProfileBody declares active_hours alone under additionalProperties:false -- so sending
        // a name to it there is a 422, not an ignored field. See #837.
        if ((await this._v2Schema.GetAsync()).ProfileRename
            && body.TryGetProperty("profile_no", out var profileNo)
            && profileNo.ValueKind == JsonValueKind.Number
            && V2ProfileBody(body, isUpdate: true) is { } v2Body
            && await this.TryV2Async(
                HttpMethod.Patch,
                "profiles-update",
                $"/api/v2/humans/{Encode(userId)}/profiles/{profileNo.GetInt32()}",
                v2Body)
                is { } reply)
        {
            await EnsureAcceptedAsync(reply.Response);

            return true;
        }

        var response = await this.SendAsync(HttpMethod.Post, $"/api/profiles/{Encode(userId)}/update", body.GetRawText());
        await EnsureAcceptedAsync(response);

        return false;
    }

    /// <summary>
    /// The body for a v2 profile create or PATCH, or null when the profile cannot be said in v2's terms
    /// and the caller should use v1.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>V2AddProfileBody</c> and <c>V2UpdateProfileBody</c> both declare <c>name</c> and
    /// <c>active_hours</c> and nothing else, under <c>additionalProperties: false</c>. The callers build
    /// one body for both surfaces, carrying <c>area</c>, <c>latitude</c> and <c>longitude</c> (which v1's
    /// create ignores, so every caller writes them to the row afterwards) and <c>profile_no</c> (which v2
    /// takes from the path). Forwarding it made every create and every rename a 422 on a server carrying
    /// PoracleNG #217, so only the two declared fields are sent.
    /// </para>
    /// <para>
    /// <c>active_hours</c> is stored as a JSON string and v2 wants the array itself; see
    /// <see cref="TryV2ActiveHours"/>. A JSON null means "not part of this request" and is said by
    /// omission on both surfaces.
    /// </para>
    /// <para>
    /// An explicit "no schedule" -- an empty string, or the <c>{}</c> PoracleNG writes for a profile that
    /// never had one -- is where create and update part. On a create, omission and <c>[]</c> both leave the
    /// new profile unscheduled, and omission is sent. On an update, v2 reads omission as "leave it
    /// unchanged", so a caller sending <c>""</c> to clear a schedule kept the old one; there it is sent as
    /// <c>[]</c>. That also clears an already-empty schedule whenever a rename resends the stored
    /// <c>{}</c>, which changes nothing.
    /// </para>
    /// </remarks>
    private static string? V2ProfileBody(JsonElement body, bool isUpdate)
    {
        var fields = new Dictionary<string, object>(StringComparer.Ordinal);

        foreach (var property in body.EnumerateObject())
        {
            if (property.NameEquals("name") && property.Value.ValueKind == JsonValueKind.String)
            {
                fields["name"] = property.Value.GetString()!;
            }
            else if (property.NameEquals("active_hours"))
            {
                if (!TryV2ActiveHours(property.Value, out var entries))
                {
                    return null;
                }

                if (entries is not null)
                {
                    fields["active_hours"] = entries;
                }
                else if (isUpdate && property.Value.ValueKind != JsonValueKind.Null)
                {
                    fields["active_hours"] = new List<Dictionary<string, int>>();
                }
            }
        }

        return JsonSerializer.Serialize(fields);
    }

    /// <summary>The bounds <c>V2ActiveHourEntry</c> declares for each of its fields.</summary>
    private static readonly Dictionary<string, (int Min, int Max)> V2ActiveHourFields = new(StringComparer.Ordinal)
    {
        ["day"] = (1, 7),
        ["hours"] = (0, 23),
        ["mins"] = (0, 59),
        ["end_hours"] = (0, 23),
        ["end_mins"] = (0, 59),
        ["step"] = (0, int.MaxValue),
    };

    /// <summary>
    /// A stored <c>active_hours</c> value as the entry list v2 declares. <paramref name="entries"/> is null
    /// for "no schedule" -- a null, an empty string, or the <c>{}</c> PoracleNG writes for a profile that
    /// never had one. False means v2 cannot take this schedule and the caller should use v1.
    /// </summary>
    /// <remarks>
    /// PoracleNG stores <c>hours</c> and <c>mins</c> as strings as often as numbers, and v2 declares
    /// integers, so numeric strings are converted. Anything else v2 would refuse -- a field it does not
    /// declare, a value outside its bounds, a missing required field -- answers false rather than being
    /// dropped or clamped: v1 has accepted these schedules for years, and reshaping one to fit would
    /// change what the user set.
    /// </remarks>
    private static bool TryV2ActiveHours(JsonElement value, out List<Dictionary<string, int>>? entries)
    {
        entries = null;
        JsonElement list;

        switch (value.ValueKind)
        {
            case JsonValueKind.Null:
                return true;

            case JsonValueKind.Array:
                list = value;
                break;

            case JsonValueKind.String:
                var text = value.GetString();
                if (string.IsNullOrWhiteSpace(text))
                {
                    return true;
                }

                try
                {
                    using var document = JsonDocument.Parse(text);
                    list = document.RootElement.Clone();
                }
                catch (JsonException)
                {
                    return false;
                }

                if (list.ValueKind == JsonValueKind.Object && !list.EnumerateObject().Any())
                {
                    return true;
                }

                if (list.ValueKind != JsonValueKind.Array)
                {
                    return false;
                }

                break;

            default:
                return false;
        }

        var result = new List<Dictionary<string, int>>();

        foreach (var item in list.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var entry = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var field in item.EnumerateObject())
            {
                if (!V2ActiveHourFields.TryGetValue(field.Name, out var bounds)
                    || !TryInteger(field.Value, out var number)
                    || number < bounds.Min
                    || number > bounds.Max)
                {
                    return false;
                }

                entry[field.Name] = number;
            }

            // step > 0 makes the entry a range, and the schema requires its end.
            var isRange = entry.TryGetValue("step", out var step) && step > 0;
            if (!entry.ContainsKey("day") || !entry.ContainsKey("hours") || !entry.ContainsKey("mins")
                || (isRange && (!entry.ContainsKey("end_hours") || !entry.ContainsKey("end_mins"))))
            {
                return false;
            }

            result.Add(entry);
        }

        entries = result;
        return true;
    }

    private static bool TryInteger(JsonElement value, out int number)
    {
        number = 0;

        // IDE0072 off: every other kind is deliberately "not an integer", and the wildcard says so.
#pragma warning disable IDE0072
        return value.ValueKind switch
        {
            JsonValueKind.Number => value.TryGetInt32(out number),
            JsonValueKind.String => int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number),
            _ => false,
        };
#pragma warning restore IDE0072
    }

    /// <summary>
    /// The profile number out of a v2 create response, or null when it does not carry one.
    /// </summary>
    /// <remarks>
    /// Null is not a failure. It means this server answered the older shape, and the caller falls back to
    /// diffing the profile list — which is what every released PoracleNG needs anyway.
    /// </remarks>
    private static int? ProfileNoFrom(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);

            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("profile_no", out var value)
                && value.ValueKind == JsonValueKind.Number
                && value.TryGetInt32(out var number)
                    ? number
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
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

    public async Task<IReadOnlyList<Human>?> ListHumansAsync(string? type = null, IReadOnlyCollection<string>? ids = null)
    {
        var capabilities = await this._v2Schema.GetAsync();
        if (!capabilities.AdminHumanRoutes)
        {
            return null;
        }

        var query = new List<string>();
        if (!string.IsNullOrEmpty(type))
        {
            query.Add($"type={Encode(type)}");
        }

        if (ids is { Count: > 0 })
        {
            query.Add($"id={string.Join(',', ids.Select(Encode))}");
        }

        var path = query.Count > 0 ? $"/api/v2/humans?{string.Join('&', query)}" : "/api/v2/humans";

        var (response, payload) = await this.SendReadAsync(HttpMethod.Get, path);
        await EnsureAcceptedAsync(response);

        using var doc = JsonDocument.Parse(payload);

        if (!doc.RootElement.TryGetProperty("humans", out var humans) || humans.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<Human>(humans.GetArrayLength());
        foreach (var item in humans.EnumerateArray())
        {
            result.Add(ParseHumanSummary(item));
        }

        return result;
    }

    /// <summary>
    /// Parses one <c>V2HumanSummary</c> item from the list endpoint. Reads <c>enabled</c>/
    /// <c>admin_disable</c> as booleans rather than through <see cref="JsonElementExtensions.GetIntProp"/>
    /// -- see <see cref="JsonElementExtensions.GetBoolAsIntProp"/> for why the two humans routes disagree
    /// on the wire shape of the same fields.
    /// </summary>
    private static Human ParseHumanSummary(JsonElement json) => new()
    {
        Id = json.GetStringProp("id"),
        Name = json.GetStringPropOrNull("name"),
        Type = json.GetStringPropOrNull("type"),
        Enabled = json.GetBoolAsIntProp("enabled"),
        Language = json.GetStringPropOrNull("language"),
        AdminDisable = json.GetBoolAsIntProp("admin_disable"),
        LastChecked = json.GetDateTimePropOrNull("last_checked") ?? default,
        DisabledDate = json.GetDateTimePropOrNull("disabled_date"),
        CurrentProfileNo = json.GetIntProp("current_profile_no"),
        Notes = json.GetStringPropOrNull("notes"),
    };

    public async Task<bool?> DeleteHumanAsync(string userId)
    {
        var capabilities = await this._v2Schema.GetAsync();
        if (!capabilities.AdminHumanRoutes)
        {
            return null;
        }

        var (response, _) = await this.SendReadAsync(HttpMethod.Delete, $"/api/v2/humans/{Encode(userId)}");

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        await EnsureAcceptedAsync(response);
        return true;
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
