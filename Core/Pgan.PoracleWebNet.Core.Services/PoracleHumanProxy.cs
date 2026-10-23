using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Models.Helpers;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Pgan.PoracleWebNet.Core.Abstractions.Services;

namespace Pgan.PoracleWebNet.Core.Services;

public partial class PoracleHumanProxy(
    HttpClient httpClient,
    IConfiguration configuration,
    IPoracleV2SchemaService v2Schema,
    ILogger<PoracleHumanProxy> logger) : IPoracleHumanProxy
{
    /// <summary>What to say when PoracleNG refused and explained nothing usable.</summary>
    private const string Unexplained = "Poracle rejected the request.";

    private readonly HttpClient _httpClient = httpClient;
    private readonly string _apiAddress = configuration["Poracle:ApiAddress"] ?? string.Empty;
    private readonly string _apiSecret = configuration["Poracle:ApiSecret"] ?? string.Empty;
    private readonly IPoracleV2SchemaService _v2Schema = v2Schema;
    private readonly ILogger<PoracleHumanProxy> _logger = logger;

    /// <summary>
    /// URL-encodes a userId for safe path construction. Webhook IDs are full URLs
    /// containing slashes that would break routing without encoding.
    /// </summary>
    private static string Encode(string userId) => Uri.EscapeDataString(userId);

    /// <summary>The release that carries every <c>/api/v2</c> route the single-human calls need.</summary>
    private const string MinimumRelease = "PoracleNG 5.4.0";

    /// <summary>
    /// Sends one request to <c>/api/v2</c>. A route the server does not have is reported as
    /// <see cref="PoracleUnsupportedException"/>; every other response is returned for the caller to read.
    /// </summary>
    /// <remarks>
    /// gin answers a route it does not have with the plaintext <c>404 page not found</c>, and the v2
    /// surface answers a missing human or place with problem+json at the same status, so the content is
    /// the only thing separating "this server is older than the minimum" from "no such human". There is
    /// no version check first and no v1 retry: the minimum is 5.4.0, and a server below it is told so
    /// rather than served a path it no longer needs. <paramref name="body"/> is sent as is when it is a
    /// string (already JSON), by raw text when a <see cref="JsonElement"/>, and serialised otherwise.
    /// </remarks>
    private async Task<HttpResponseMessage> SendV2Async(
        HttpMethod method, string path, string feature, object? body = null)
    {
        var json = body switch
        {
            null => null,
            string raw => raw,
            JsonElement element => element.GetRawText(),
            _ => JsonSerializer.Serialize(body),
        };

        var response = await this.SendAsync(method, path, json);

        if (response.StatusCode == HttpStatusCode.NotFound
            && !PoracleProblemDetails.IsProblemJson(await response.Content.ReadAsStringAsync()))
        {
            throw new PoracleUnsupportedException(feature, MinimumRelease);
        }

        return response;
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
    /// 422 is the same refusal wearing a different number. Verified live: <c>/api/v2/humans</c> answers
    /// RFC 9457 problem+json at 422 for these mistakes, where the v1 routes this class once called
    /// answered 400. Matching only 400 would re-open this.
    /// </para>
    /// <para>
    /// A 404 is account-gone only when PoracleNG says so. It also answers 404 "Profile not found" when a
    /// profile number does not exist, and gin answers a plaintext "404 page not found" for a route this
    /// build does not have -- treating either as a dead account signed the user out of a working session,
    /// and in <c>ProfileOverviewService</c>'s restore-the-profile <c>finally</c> it did so while
    /// swallowing the real failure. Both verified against 5.2.1 and 5.4.0.
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
    /// "human not found" (huma's <c>Error404NotFound("human not found")</c>, matched exactly as every other proxy
    /// matches it). Anything else at 404 names a profile, a place or a route.
    /// </remarks>
    private static bool NamesAMissingAccount(string? payload) =>
        payload is not null
        && (payload.Contains("user not found", StringComparison.OrdinalIgnoreCase)
            || payload.Contains("human not found", StringComparison.Ordinal));

    public async Task<JsonElement?> GetHumanAsync(string userId)
    {
        var response = await this.SendV2Async(HttpMethod.Get, $"/api/v2/humans/{Encode(userId)}", "reading a user");

        // A 404 is PoracleNG answering: no such human (problem+json "human not found"; a route that is
        // merely absent never gets here, SendV2Async has already refused it). Anything else non-2xx is
        // PoracleNG failing to answer, and null for it -- what
        // this returned for every status -- told every caller the account was gone: the purge answered
        // "already deleted" during an outage and /api/auth/me signed live users out. The same contract as
        // GetAdminRolesAsync. See #994.
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        // PoracleNG wraps the response: { "human": { ... } }
        if (doc.RootElement.TryGetProperty("human", out var human))
        {
            return human.Clone();
        }

        return doc.RootElement.Clone();
    }

    /// <summary>
    /// The properties <c>V2CreateHumanBody</c> declares. The body is <c>additionalProperties:false</c>, so
    /// anything else is a 422; checked against the 5.4.0 schema in both directions by the proxy tests.
    /// </summary>
    private static readonly string[] CreateHumanProperties =
    [
        "admin_disable", "area", "community", "enabled", "id", "language",
        "latitude", "longitude", "name", "notes", "profile_name", "type",
    ];

    public async Task CreateHumanAsync(JsonElement body)
    {
        // Only what v2 declares goes out, and only what the caller supplied: every other field takes
        // PoracleNG's default, so an absent key has to stay absent.
        var declared = new Dictionary<string, JsonElement>();
        if (body.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in body.EnumerateObject())
            {
                if (Array.IndexOf(CreateHumanProperties, property.Name) >= 0)
                {
                    declared[property.Name] = property.Value;
                }
            }
        }

        var response = await this.SendV2Async(HttpMethod.Post, "/api/v2/humans", "creating a user", declared);
        await EnsureAcceptedAsync(response);
    }

    public async Task StartAsync(string userId)
    {
        var response = await this.SendV2Async(
            HttpMethod.Post, $"/api/v2/humans/{Encode(userId)}/enable", "enabling a user");

        await EnsureAcceptedAsync(response);
    }

    public async Task StopAsync(string userId)
    {
        var response = await this.SendV2Async(
            HttpMethod.Post, $"/api/v2/humans/{Encode(userId)}/disable", "disabling a user");

        await EnsureAcceptedAsync(response);
    }

    /// <inheritdoc />
    public async Task SetLanguageAsync(string userId, string language)
    {
        var response = await this.SendV2Async(
            HttpMethod.Post,
            $"/api/v2/humans/{Encode(userId)}/language",
            "setting a user's language",
            new { language });

        await EnsureAcceptedAsync(response);
    }

    public async Task AdminDisabledAsync(string userId, bool disabled)
    {
        // v2's adminDisableBody is `Disabled *bool`; v1's `state` is a 422 there.
        var response = await this.SendV2Async(
            HttpMethod.Post,
            $"/api/v2/humans/{Encode(userId)}/admin-disable",
            "banning a user",
            new { disabled });

        await EnsureAcceptedAsync(response);
    }

    public async Task SetLocationAsync(string userId, double lat, double lon)
    {
        var response = await this.SendV2Async(
            HttpMethod.Post,
            $"/api/v2/humans/{Encode(userId)}/location",
            "setting a user's location",
            LocationBody(lat, lon));

        await EnsureAcceptedAsync(response);
    }

    /// <inheritdoc />
    public async Task<string?> GetAdminRolesAsync(string userId)
    {
        var response = await this.SendV2Async(
            HttpMethod.Get,
            $"/api/v2/humans/{Encode(userId)}/admin-roles",
            "reading a user's delegated webhooks");

        // A 404 is PoracleNG answering: this human has no roles because it has no such human. Anything
        // else non-2xx is PoracleNG failing to answer, and returning null for it -- which is what this
        // did for every status -- told UserRoleResolver "no delegated webhooks" confidently enough to
        // cache for a minute. That is #656 and #667 on the one source their fix did not cover.
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>The <c>rejected</c> list out of a v2 areas answer, empty when there is none to read.</summary>
    private static List<string> RejectedAreas(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);

            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("rejected", out var rejected)
                && rejected.ValueKind == JsonValueKind.Array
                    ? [.. rejected.EnumerateArray()
                        .Where(e => e.ValueKind == JsonValueKind.String)
                        .Select(e => e.GetString()!)]
                    : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public async Task<JsonElement?> GetAreasAsync(string userId) =>
        // The user's selected areas are the human's own area field; the available list is a separate route.
        await this.GetHumanAsync(userId);

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> SetAreasTrustedAsync(string userId, IReadOnlyCollection<string> areas)
    {
        var body = JsonSerializer.Serialize(new { areas, trusted = true });
        var response = await this.SendV2Async(HttpMethod.Post, $"/api/v2/humans/{Encode(userId)}/areas", "setting areas", body);
        await EnsureAcceptedAsync(response);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var rejected = StringList(doc.RootElement, "rejected");
        if (rejected.Count > 0)
        {
            // Trusted lifts userSelectable, not "exists at all": a rejected name is a fence PoracleNG has
            // not loaded or a stale entry. The caller decides what that means for the save. Debug, not
            // warning: production holds dead names (30 users, 69 names on 2026-10-21) that come back
            // here on every save and toggle.
            LogTrustedAreasRejected(this._logger, userId, string.Join(", ", rejected));
        }

        return rejected;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetAreaListAsync(string userId)
    {
        // GetHumanAsync throws on any failed answer except a missing human, which is null. A missing
        // human has no list to read, and answering empty would let a caller write a replacement over it.
        var human = await this.GetHumanAsync(userId) ?? throw new AccountGoneException();
        var areaJson = human.GetAreaListJsonOrNull("area");
        return AreaListJson.Parse(areaJson).Select(a => a.ToLowerInvariant()).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<V2AreaInfo>> GetAvailableAreasAsync(string userId)
    {
        var response = await this.SendV2Async(HttpMethod.Get, $"/api/v2/humans/{Encode(userId)}/areas", "listing areas");
        await EnsureAcceptedAsync(response);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (!doc.RootElement.TryGetProperty("areas", out var areas) || areas.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return areas.EnumerateArray()
            .Where(a => a.ValueKind == JsonValueKind.Object && a.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String)
            .Select(a => new V2AreaInfo(
                a.GetProperty("name").GetString()!,
                BoolOrFalse(a, "userSelectable"),
                BoolOrFalse(a, "exemptFromAreaSecurity")))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<AreaRemovalResult> RemoveAreaEverywhereAsync(string userId, string name, bool dryRun)
    {
        var path = $"/api/v2/humans/{Encode(userId)}/areas?name={Uri.EscapeDataString(name)}&all_profiles=true"
            + (dryRun ? "&dry_run=true" : string.Empty);
        var response = await this.SendV2Async(HttpMethod.Delete, path, "removing an area");
        await EnsureAcceptedAsync(response);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        var broadened = new Dictionary<string, IReadOnlyList<int>>(StringComparer.Ordinal);
        if (root.TryGetProperty("broadened", out var map) && map.ValueKind == JsonValueKind.Object)
        {
            foreach (var type in map.EnumerateObject())
            {
                broadened[type.Name] = IntList(type.Value);
            }
        }

        return new AreaRemovalResult(
            root.TryGetProperty("removed_from", out var removed) ? IntList(removed) : [],
            broadened,
            BoolOrFalse(root, "dry_run"));
    }

    /// <inheritdoc />
    public async Task<AreaRenameResult> RenameAreaEverywhereAsync(string userId, string from, string to, bool dryRun)
    {
        var path = $"/api/v2/humans/{Encode(userId)}/areas?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}"
            + "&all_profiles=true&trusted=true" + (dryRun ? "&dry_run=true" : string.Empty);
        var response = await this.SendV2Async(HttpMethod.Patch, path, "renaming an area");
        await EnsureAcceptedAsync(response);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        return new AreaRenameResult(
            root.TryGetProperty("renamed_in", out var renamed) ? IntList(renamed) : [],
            BoolOrFalse(root, "dry_run"));
    }

    private static bool BoolOrFalse(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;

    private static List<int> IntList(JsonElement element) =>
        element.ValueKind == JsonValueKind.Array
            ? element.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Number).Select(e => e.GetInt32()).ToList()
            : [];

    private static List<string> StringList(JsonElement root, string property) =>
        root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToList()
            : [];

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// Areas first, then location, each addressed to the profile by <c>?profile=</c>. Verified on the 5.3.0
    /// bed that both write the named profile's row and leave <c>humans.area</c>, <c>humans.latitude</c>,
    /// <c>humans.longitude</c> and every other profile alone. That bed also answers 200 and writes nothing
    /// for a profile number that does not exist, so a write here cannot report "no such profile"; the
    /// callers pass the number PoracleNG has just assigned.
    /// </para>
    /// <para>
    /// There is no direct database write behind this. A non-empty list is always sent <c>trusted</c>, so a
    /// duplicated profile keeps the user's own drawn fences. Names PoracleNG rejects are logged and
    /// dropped. A refusal of either write throws, and the caller decides what to do about a profile
    /// that already exists.
    /// </para>
    /// </remarks>
    public async Task SetProfileGeographyAsync(
        string userId, int profileNo, IReadOnlyCollection<string> areas, double latitude, double longitude)
    {
        var requested = areas
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Select(a => a.ToLowerInvariant())
            .Distinct()
            .ToList();

        // A user's own fences are userSelectable:false and would come back rejected without trusted.
        var trusted = requested.Count > 0;

        var areasBody = trusted
            ? JsonSerializer.Serialize(new { areas = requested, trusted = true })
            : JsonSerializer.Serialize(new { areas = requested });

        var areasResponse = await this.SendV2Async(
            HttpMethod.Post, $"/api/v2/humans/{Encode(userId)}/areas?profile={profileNo}", "saving a profile's areas", areasBody);
        await EnsureAcceptedAsync(areasResponse);

        // A refused name is one PoracleNG has not loaded (a stale area on the source profile). It is
        // dropped, not fatal: the profile and its location are worth more than the one area, and the user can add it back from the Areas page.
        var refused = RejectedAreas(await areasResponse.Content.ReadAsStringAsync())
            .Where(r => requested.Contains(r, StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (refused.Count > 0)
        {
            LogProfileAreasRefused(this._logger, userId, profileNo, string.Join(", ", refused));
        }

        var locationResponse = await this.SendV2Async(
            HttpMethod.Post,
            $"/api/v2/humans/{Encode(userId)}/location?profile={profileNo}",
            "saving a profile's location",
            LocationBody(latitude, longitude));
        await EnsureAcceptedAsync(locationResponse);
    }

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "PoracleNG has no fence by {Rejected} for {UserId}'s new profile {ProfileNo}; they were dropped")]
    private static partial void LogProfileAreasRefused(
        ILogger logger, string userId, int profileNo, string rejected);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "setAreas(trusted) for {UserId}: PoracleNG has no fence by these names, so it left them out: {Rejected}")]
    private static partial void LogTrustedAreasRejected(ILogger logger, string userId, string rejected);

    public async Task SwitchProfileAsync(string userId, int profileNo)
    {
        var response = await this.SendV2Async(
            HttpMethod.Post,
            $"/api/v2/humans/{Encode(userId)}/profile",
            "switching profile",
            JsonSerializer.Serialize(new { profile_no = profileNo }));

        await EnsureAcceptedAsync(response);
    }

    /// <remarks>
    /// Answers v1's shape -- <c>{"profile":[...]}</c>, with <c>area</c> and <c>active_hours</c> as the
    /// JSON-encoded strings v1 stored -- so <c>ProfileService</c> and everything downstream of
    /// <see cref="Profile"/> stay as they are. See <see cref="V1ProfilesFromV2"/>.
    /// </remarks>
    public async Task<JsonElement> GetProfilesAsync(string userId)
    {
        var response = await this.SendV2Async(
            HttpMethod.Get, $"/api/v2/humans/{Encode(userId)}/profiles", "reading profiles");
        await EnsureAcceptedAsync(response);

        return V1ProfilesFromV2(await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// A v2 profile list (<c>V2ProfilesOutputBody</c>) restated in v1's shape.
    /// </summary>
    /// <remarks>
    /// <para>
    /// v1 answers <c>{"profile":[...],"status":"ok"}</c> with <c>area</c> and <c>active_hours</c> as
    /// JSON-encoded strings. v2 answers <c>{"profiles":[...]}</c>, sends <c>active_hours</c> as the typed
    /// entry array on every version that has the route, and sends <c>area</c> as a string on 5.2.1 and as
    /// a real array since jfberry/PoracleNG#303. A string passes through untouched; an array is encoded
    /// the way v1 stores it. Translated here, once, rather than taught to every reader of
    /// <see cref="Profile.Area"/> and <see cref="Profile.ActiveHours"/>.
    /// </para>
    /// <para>
    /// One difference survives and is harmless: v2 has already turned string-typed <c>hours</c>/<c>mins</c>
    /// into integers and answers an unscheduled profile as <c>[]</c> where v1 might answer the stored
    /// <c>{}</c>. Both read as the same schedule everywhere a schedule is read.
    /// </para>
    /// </remarks>
    internal static JsonElement V1ProfilesFromV2(string payload)
    {
        using var doc = JsonDocument.Parse(payload);
        var profiles = new List<Dictionary<string, object?>>();

        if (doc.RootElement.ValueKind == JsonValueKind.Object
            && doc.RootElement.TryGetProperty("profiles", out var list)
            && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in list.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var profile = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (var property in item.EnumerateObject())
                {
                    profile[property.Name] = property.NameEquals("area") || property.NameEquals("active_hours")
                        ? EncodedAsV1(property.Value)
                        : property.Value.Clone();
                }

                profiles.Add(profile);
            }
        }

        return JsonSerializer.SerializeToElement(new Dictionary<string, object>
        {
            ["profile"] = profiles,
            ["status"] = "ok",
        });
    }

    /// <summary>An array as the JSON text v1 stores it in; a string or null unchanged.</summary>
    private static object? EncodedAsV1(JsonElement value) =>
        value.ValueKind == JsonValueKind.Array ? JsonSerializer.Serialize(value) : value.Clone();

    /// <inheritdoc />
    public async Task<int> AddProfileAsync(string userId, JsonElement body)
    {
        var v2Body = V2ProfileBody(body, isUpdate: false)
            ?? throw new PoracleRequestRefusedException(UnsendableSchedule);

        var response = await this.SendV2Async(
            HttpMethod.Post, $"/api/v2/humans/{Encode(userId)}/profiles", "creating a profile", v2Body);
        await EnsureAcceptedAsync(response);

        // 5.4.0 reports the number it assigned (PoracleNG picks the lowest free one, so it cannot be
        // predicted). A 200 without it is a server we cannot safely carry on with: guessing would file the
        // profile's alarms under a number that may belong to nothing (#407). The profile row already exists
        // by now and nothing here deletes it, so a 200 without profile_no would leave it behind. 5.4.0's
        // create always reports the number, which is why no rollback is written; if that stops being true
        // the profile needs removing here.
        return ProfileNoFrom(await response.Content.ReadAsStringAsync())
            ?? throw new PoracleRequestRefusedException(
                "PoracleNG created the profile but did not report its profile number.",
                (int)HttpStatusCode.BadGateway);
    }

    /// <inheritdoc />
    public async Task UpdateProfileAsync(string userId, JsonElement body)
    {
        if (!body.TryGetProperty("profile_no", out var profileNo)
            || profileNo.ValueKind != JsonValueKind.Number
            || !profileNo.TryGetInt32(out var number))
        {
            throw new ArgumentException("An update must say which profile it is for (profile_no).", nameof(body));
        }

        var v2Body = V2ProfileBody(body, isUpdate: true)
            ?? throw new PoracleRequestRefusedException(UnsendableSchedule);

        var response = await this.SendV2Async(
            HttpMethod.Patch, $"/api/v2/humans/{Encode(userId)}/profiles/{number}", "updating a profile", v2Body);
        await EnsureAcceptedAsync(response);
    }

    private const string UnsendableSchedule =
        "The profile's active hours are not in a form PoracleNG accepts (day 1-7, hours 0-23, mins 0-59).";

    /// <summary>
    /// The body for a v2 profile create or PATCH, or null when the profile's schedule cannot be said in v2's
    /// terms, which the callers report as a refusal.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>V2AddProfileBody</c> and <c>V2UpdateProfileBody</c> both declare <c>name</c> and
    /// <c>active_hours</c> and nothing else, under <c>additionalProperties: false</c>. The callers build
    /// one body for both surfaces, carrying <c>area</c>, <c>latitude</c> and <c>longitude</c> (which the
    /// create ignores, so every caller sets them afterwards through the ?profile= writes) and <c>profile_no</c> (which v2
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
    /// <c>[]</c>. That also clears an already-empty schedule when a caller sends <c>{}</c>, which
    /// changes nothing.
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
    /// never had one. False means v2 cannot take this schedule, which the callers report as a refusal.
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
    /// Null means the answer carried no number; <see cref="AddProfileAsync"/> turns that into a refusal.
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

    /// <remarks>
    /// Cascades the profile's tracking rules and moves <c>current_profile_no</c> when the active profile
    /// goes (#995). A human that does not exist is a 404 problem+json, which becomes
    /// <see cref="AccountGoneException"/>.
    /// </remarks>
    public async Task DeleteProfileAsync(string userId, int profileNo)
    {
        var response = await this.SendV2Async(
            HttpMethod.Delete, $"/api/v2/humans/{Encode(userId)}/profiles/{profileNo}", "deleting a profile");

        await EnsureAcceptedAsync(response);
    }

    /// <remarks>
    /// Replaces every rule on <paramref name="toProfileNo"/> with a copy of <paramref name="fromProfileNo"/>'s.
    /// Copying from a profile with no rules therefore empties the destination.
    /// </remarks>
    public async Task CopyProfileAsync(string userId, int fromProfileNo, int toProfileNo)
    {
        var response = await this.SendV2Async(
            HttpMethod.Post,
            $"/api/v2/humans/{Encode(userId)}/profiles/{toProfileNo}/copy",
            "copying a profile",
            JsonSerializer.Serialize(new { from_profile = fromProfileNo }));

        await EnsureAcceptedAsync(response);
    }

    public async Task<SavedPlaces> GetPlacesAsync(string userId)
    {
        var response = await this.SendV2Async(
            HttpMethod.Get, $"/api/v2/humans/{Encode(userId)}/locations", "reading saved places");
        var json = await response.Content.ReadAsStringAsync();

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
        // something to show a person who typed a long name. user_locations.label is varchar(64).
        if (place.Label is { Length: > 64 })
        {
            return "That name is too long. Use 64 characters or fewer.";
        }

        var response = await this.SendV2Async(
            HttpMethod.Post,
            $"/api/v2/humans/{Encode(userId)}/locations",
            "saving a place",
            LocationBody(place.Latitude, place.Longitude, place.Label));

        // v2 turns the duplicate label into a real 409 instead of burying it in a 200. Returned as a
        // message so the controller and the SPA see a refusal they can show, not an exception.
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            return PoracleProblemDetails.Describe(await response.Content.ReadAsStringAsync(), Unexplained);
        }

        await EnsureAcceptedAsync(response);
        return null;
    }

    /// <inheritdoc />
    public async Task UpdatePlaceAsync(string userId, string label, double latitude, double longitude)
    {
        var response = await this.SendV2Async(
            HttpMethod.Put,
            $"/api/v2/humans/{Encode(userId)}/locations/{Encode(label)}",
            "moving a saved place",
            LocationBody(latitude, longitude));

        await EnsureAcceptedAsync(response);
    }

    /// <summary>
    /// The request body for the v2 location writes. <c>V2LocationBody</c> and its two siblings are
    /// <c>additionalProperties:false</c> and name the coordinates <c>latitude</c>/<c>longitude</c> on
    /// 5.4.0 (jfberry/PoracleNG#303); <c>lat</c>/<c>lon</c> is a 422. <c>JsonSerializer</c> is
    /// culture-invariant, so the numbers cannot pick up a comma decimal separator.
    /// </summary>
    private static string LocationBody(double latitude, double longitude, string? label = null)
    {
        var body = new Dictionary<string, object?>();

        if (label is not null)
        {
            body["label"] = label;
        }

        body["latitude"] = latitude;
        body["longitude"] = longitude;
        return JsonSerializer.Serialize(body);
    }

    public async Task DeletePlaceAsync(string userId, string label)
    {
        var response = await this.SendV2Async(
            HttpMethod.Delete,
            $"/api/v2/humans/{Encode(userId)}/locations/{Encode(label)}",
            "deleting a saved place");

        // Read before the general refusal path: a 409 here names the alarms still pointing at the place,
        // which is the difference between "could not delete" and knowing what to repoint first.
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            throw new PlaceInUseException(ReferencingRules(await response.Content.ReadAsStringAsync()));
        }

        await EnsureAcceptedAsync(response);
    }

    public async Task<IReadOnlyList<Human>> ListHumansAsync(string? type = null, IReadOnlyCollection<string>? ids = null)
    {
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

        var response = await this.SendV2Async(HttpMethod.Get, path, "the admin user list");
        await EnsureAcceptedAsync(response);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

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

    public async Task<bool> DeleteHumanAsync(string userId)
    {
        var response = await this.SendV2Async(
            HttpMethod.Delete, $"/api/v2/humans/{Encode(userId)}", "deleting a user");

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
}
