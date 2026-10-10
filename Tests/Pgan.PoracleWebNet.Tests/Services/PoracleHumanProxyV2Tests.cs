using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// The human, location and place operations on PoracleNG's <c>/api/v2</c> surface, and every way they
/// must fall back to v1 instead.
/// </summary>
/// <remarks>
/// <para>
/// Every response here was taken from a live 5.2.1 and, for the fallbacks, a live 5.1.0: the
/// <c>{"human":{...}}</c> wrapper that both versions share, the 409 on a duplicate place label, the 404
/// problem+json for a place that does not exist, and gin's plaintext <c>404 page not found</c> for a
/// route the build has never had.
/// </para>
/// <para>
/// Half of these assert that a 5.1.0 self-hoster is unaffected. That is the point of keeping v1: there
/// is no version floor, so the fallback has to be exercised as hard as the new path.
/// </para>
/// </remarks>
public class PoracleHumanProxyV2Tests
{
    private const string ApiAddress = "http://localhost:3030";

    /// <summary>What gin answers for a route this build does not carry. Plaintext, not problem+json.</summary>
    private const string RouteMissing = "404 page not found";

    [Fact]
    public async Task GetHumanReadsTheV2RouteOn521()
    {
        var handler = ScriptedHandler.Ok("""{"human":{"id":"user1","language":"en"}}""");
        var sut = CreateSut(handler, "5.2.1");

        var human = await sut.GetHumanAsync("user1");

        var request = Assert.Single(handler.Requests);
        Assert.Equal($"{ApiAddress}/api/v2/humans/user1", request.Url);
        Assert.Equal("user1", human!.Value.GetProperty("id").GetString());
    }

    [Fact]
    public async Task GetHumanUsesV1OnAServerWithoutV2()
    {
        var handler = ScriptedHandler.Ok("""{"human":{"id":"user1"},"status":"ok"}""");
        var sut = CreateSut(handler, "5.1.0");

        await sut.GetHumanAsync("user1");

        Assert.Equal($"{ApiAddress}/api/humans/one/user1", Assert.Single(handler.Requests).Url);
    }

    [Fact]
    public async Task AVersionThatClaimsV2ButHasNoRouteFallsBackToV1()
    {
        // A fork can carry a version number without the routes. The plaintext 404 is the only signal,
        // and answering it with a failure rather than a retry would take the operation away entirely.
        var handler = new ScriptedHandler(
            new Reply(HttpStatusCode.NotFound, RouteMissing, "text/plain"),
            new Reply(HttpStatusCode.OK, """{"human":{"id":"user1"},"status":"ok"}"""));
        var sut = CreateSut(handler, "5.2.1");

        var human = await sut.GetHumanAsync("user1");

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal($"{ApiAddress}/api/v2/humans/user1", handler.Requests[0].Url);
        Assert.Equal($"{ApiAddress}/api/humans/one/user1", handler.Requests[1].Url);
        Assert.Equal("user1", human!.Value.GetProperty("id").GetString());
    }

    [Fact]
    public async Task AMissingHumanIsNotMistakenForAMissingRoute()
    {
        // Both are 404. Only the content type separates them, and reading the problem+json one as a
        // missing route would drop the whole surface to v1 for five minutes over a stale user id.
        var handler = ScriptedHandler.Problem(
            HttpStatusCode.NotFound, """{"title":"Not Found","status":404,"detail":"human not found"}""");
        var sut = CreateSut(handler, "5.2.1");

        Assert.Null(await sut.GetHumanAsync("nobody"));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task OneMissingRouteDoesNotDropTheOthersToV1()
    {
        // The absence is remembered per route. A single shared flag would let a 404 on one route send
        // every other call back to v1 -- and PUT /locations/{label} has no v1 path at all, so for that
        // one "fall back" means the feature disappears.
        var cache = new MemoryCache(new MemoryCacheOptions());
        var handler = new ScriptedHandler(
            new Reply(HttpStatusCode.NotFound, RouteMissing, "text/plain"),
            new Reply(HttpStatusCode.OK, """{"status":"ok"}"""));

        await CreateSut(handler, "5.2.1", cache).StartAsync("user1");
        await CreateSut(handler, "5.2.1", cache).StopAsync("user1");

        Assert.Equal($"{ApiAddress}/api/v2/humans/user1/enable", handler.Requests[0].Url);
        Assert.Equal($"{ApiAddress}/api/humans/user1/start", handler.Requests[1].Url);
        Assert.Equal($"{ApiAddress}/api/v2/humans/user1/disable", handler.Requests[2].Url);
    }

    [Fact]
    public async Task AMissingRouteIsNotProbedAgainWhileItIsRemembered()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var handler = new ScriptedHandler(
            new Reply(HttpStatusCode.NotFound, RouteMissing, "text/plain"),
            new Reply(HttpStatusCode.OK, """{"status":"ok"}"""));

        await CreateSut(handler, "5.2.1", cache).StartAsync("user1");
        await CreateSut(handler, "5.2.1", cache).StartAsync("user1");

        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal($"{ApiAddress}/api/humans/user1/start", handler.Requests[2].Url);
    }

    [Fact]
    public async Task SetLanguageSendsTheSameBodyToWhicheverRouteAnswers()
    {
        var v2 = ScriptedHandler.Ok("""{"status":"ok"}""");
        await CreateSut(v2, "5.2.1").SetLanguageAsync("user1", "de");

        var sent = Assert.Single(v2.Requests);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal($"{ApiAddress}/api/v2/humans/user1/language", sent.Url);
        Assert.Equal("de", JsonDocument.Parse(sent.Body!).RootElement.GetProperty("language").GetString());

        var v1 = ScriptedHandler.Ok("""{"language":"de","status":"ok"}""");
        await CreateSut(v1, "5.1.0").SetLanguageAsync("user1", "de");

        // Identical body on both, which is the whole reason this one needed no translation.
        var legacy = Assert.Single(v1.Requests);
        Assert.Equal($"{ApiAddress}/api/humans/user1/language", legacy.Url);
        Assert.Equal("de", JsonDocument.Parse(legacy.Body!).RootElement.GetProperty("language").GetString());
    }

    [Fact]
    public async Task ARefusedLanguageIsReportedAsARefusal()
    {
        // Where general.available_languages is configured, an unlisted code is refused. It has to reach
        // the user as their input being wrong, not as the server having broken. See #539.
        var handler = ScriptedHandler.Problem(
            HttpStatusCode.UnprocessableEntity,
            """{"title":"Unprocessable Entity","status":422,"detail":"language is required"}""");

        var refused = await Assert.ThrowsAsync<PoracleRequestRefusedException>(
            () => CreateSut(handler, "5.2.1").SetLanguageAsync("user1", ""));

        Assert.Contains("language is required", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AdminDisableRenamesTheKeyAsWellAsTheRoute()
    {
        // v2's body is `disabled`; v1's is `state`. Sending v1's key to v2 is a 422, and sending v2's
        // key to v1 is "state is required (true/false)" -- the defect the v1 body already exists to fix.
        var v2 = ScriptedHandler.Ok("""{"status":"ok"}""");
        await CreateSut(v2, "5.2.1").AdminDisabledAsync("user1", true);

        var sent = Assert.Single(v2.Requests);
        Assert.Equal($"{ApiAddress}/api/v2/humans/user1/admin-disable", sent.Url);
        var v2Body = JsonDocument.Parse(sent.Body!).RootElement;
        Assert.True(v2Body.GetProperty("disabled").GetBoolean());
        Assert.False(v2Body.TryGetProperty("state", out _));

        var v1 = ScriptedHandler.Ok("""{"status":"ok"}""");
        await CreateSut(v1, "5.1.0").AdminDisabledAsync("user1", true);

        var legacy = Assert.Single(v1.Requests);
        Assert.Equal($"{ApiAddress}/api/humans/user1/adminDisabled", legacy.Url);
        Assert.True(JsonDocument.Parse(legacy.Body!).RootElement.GetProperty("state").GetBoolean());
    }

    [Fact]
    public async Task SetLocationMovesTheCoordinatesIntoTheBody()
    {
        var handler = ScriptedHandler.Ok("""{"status":"ok"}""");
        await CreateSut(handler, "5.2.1").SetLocationAsync("user1", 51.5, -0.12);

        var sent = Assert.Single(handler.Requests);
        Assert.Equal($"{ApiAddress}/api/v2/humans/user1/location", sent.Url);
        var body = JsonDocument.Parse(sent.Body!).RootElement;
        Assert.Equal(51.5, body.GetProperty("lat").GetDouble());
        Assert.Equal(-0.12, body.GetProperty("lon").GetDouble());
    }

    [Fact]
    public async Task TheV1LocationPathIsInvariantWhateverTheServerCultureIs()
    {
        // The v1 path interpolates the doubles. Under a culture with a comma decimal separator that
        // produced /setLocation/51,5/-0,12 -- four path segments where PoracleNG routes two.
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            var handler = ScriptedHandler.Ok("""{"status":"ok"}""");
            await CreateSut(handler, "5.1.0").SetLocationAsync("user1", 51.5, -0.12);

            Assert.Equal(
                $"{ApiAddress}/api/humans/user1/setLocation/51.5/-0.12",
                Assert.Single(handler.Requests).Url);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public async Task SwitchProfileMovesTheNumberIntoTheBody()
    {
        var handler = ScriptedHandler.Ok("""{"status":"ok"}""");
        await CreateSut(handler, "5.2.1").SwitchProfileAsync("user1", 3);

        var sent = Assert.Single(handler.Requests);
        Assert.Equal($"{ApiAddress}/api/v2/humans/user1/profile", sent.Url);
        Assert.Equal(3, JsonDocument.Parse(sent.Body!).RootElement.GetProperty("profile_no").GetInt32());
    }

    [Fact]
    public async Task AddPlaceSendsLatLonWhereTheListAnswersLatitudeLongitude()
    {
        // The asymmetry is upstream's, not a typo: the request takes lat/lon, the read answers the
        // long names.
        var handler = ScriptedHandler.Ok("""{"status":"ok"}""");
        var sut = CreateSut(handler, "5.2.1");

        var refusal = await sut.AddPlaceAsync(
            "user1", new SavedPlace { Label = "home", Latitude = 1.5, Longitude = 2.5 });

        Assert.Null(refusal);
        var sent = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal($"{ApiAddress}/api/v2/humans/user1/locations", sent.Url);
        var body = JsonDocument.Parse(sent.Body!).RootElement;
        Assert.Equal("home", body.GetProperty("label").GetString());
        Assert.Equal(1.5, body.GetProperty("lat").GetDouble());
        Assert.Equal(2.5, body.GetProperty("lon").GetDouble());
        Assert.False(body.TryGetProperty("latitude", out _));
    }

    [Fact]
    public async Task ADuplicateLabelIsReportedNotThrown()
    {
        // v2 answers a real 409 where v1 buried the same refusal in a 200 body. Both have to reach the
        // caller as the same string, because the controller turns it into the message shown by the field.
        var handler = ScriptedHandler.Problem(
            HttpStatusCode.Conflict,
            """{"title":"Conflict","status":409,"detail":"location label already exists"}""");
        var sut = CreateSut(handler, "5.2.1");

        var refusal = await sut.AddPlaceAsync("user1", new SavedPlace { Label = "home" });

        Assert.Equal("location label already exists", refusal);
    }

    [Fact]
    public async Task ALabelTooLongForTheColumnIsRefusedBeforeItIsSent()
    {
        // humans_locations.label is varchar(64). v1 reported the overflow as "Data too long for column
        // 'label'" inside a 200 and v2 answers 500 {"detail":"database error"} -- both verified live,
        // and neither is a sentence to show someone who typed a long name.
        var handler = ScriptedHandler.Ok("""{"status":"ok"}""");
        var sut = CreateSut(handler, "5.2.1");

        var refusal = await sut.AddPlaceAsync("user1", new SavedPlace { Label = new string('x', 65) });

        Assert.NotNull(refusal);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ALabelOfExactlySixtyFourStillSaves()
    {
        var handler = ScriptedHandler.Ok("""{"status":"ok"}""");
        var sut = CreateSut(handler, "5.2.1");

        Assert.Null(await sut.AddPlaceAsync("user1", new SavedPlace { Label = new string('x', 64) }));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task AddPlaceStillReadsTheV1ResultsArrayOnAnOlderServer()
    {
        var handler = ScriptedHandler.Ok("""{"results":[{"error":"label already used"}],"status":"ok"}""");
        var sut = CreateSut(handler, "5.1.0");

        Assert.Equal("label already used", await sut.AddPlaceAsync("user1", new SavedPlace { Label = "home" }));
        Assert.Equal($"{ApiAddress}/api/humans/user1/locations/add", Assert.Single(handler.Requests).Url);
    }

    [Fact]
    public async Task UpdatePlacePutsTheNewCoordinates()
    {
        var handler = ScriptedHandler.Ok("""{"status":"ok"}""");
        var sut = CreateSut(handler, "5.2.1");

        Assert.True(await sut.UpdatePlaceAsync("user1", "home", 9.5, 8.5));

        var sent = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Put, sent.Method);
        Assert.Equal($"{ApiAddress}/api/v2/humans/user1/locations/home", sent.Url);
        var body = JsonDocument.Parse(sent.Body!).RootElement;
        Assert.Equal(9.5, body.GetProperty("lat").GetDouble());
        Assert.Equal(8.5, body.GetProperty("lon").GetDouble());
    }

    [Fact]
    public async Task UpdatePlaceAnswersFalseRatherThanFailingOnAServerWithoutTheRoute()
    {
        // There is no v1 equivalent, so the honest answer is "this server cannot", not an exception the
        // SPA would show as a failed save.
        var handler = ScriptedHandler.Ok("""{"status":"ok"}""");
        var sut = CreateSut(handler, "5.1.0");

        Assert.False(await sut.UpdatePlaceAsync("user1", "home", 9.5, 8.5));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task UpdatingAPlaceThatIsNotThereIsARefusalNotAMissingRoute()
    {
        var handler = ScriptedHandler.Problem(
            HttpStatusCode.NotFound, """{"title":"Not Found","status":404,"detail":"location not found"}""");
        var sut = CreateSut(handler, "5.2.1");

        var refused = await Assert.ThrowsAsync<PoracleRequestRefusedException>(
            () => sut.UpdatePlaceAsync("user1", "nope", 1, 2));

        Assert.Equal(404, refused.StatusCode);
        Assert.Contains("location not found", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetPlacesReadsTheSameWrapperFromBothSurfaces()
    {
        const string Body =
            """{"locations":{"default":{"latitude":1,"longitude":2},"named":[{"label":"home","latitude":3,"longitude":4}]}}""";

        foreach (var (version, url) in new[]
        {
            ("5.2.1", $"{ApiAddress}/api/v2/humans/user1/locations"),
            ("5.1.0", $"{ApiAddress}/api/humans/user1/locations"),
        })
        {
            var handler = ScriptedHandler.Ok(Body);
            var places = await CreateSut(handler, version).GetPlacesAsync("user1");

            Assert.Equal(url, Assert.Single(handler.Requests).Url);
            Assert.Equal("home", Assert.Single(places.Named).Label);
            Assert.Equal(1, places.Default!.Latitude);
        }
    }

    [Fact]
    public async Task AdminRolesReadsWhicheverRouteTheServerHas()
    {
        // The response is identical apart from v1's extra "status":"ok", because v2's handler calls the
        // same delegated-administration logic. Verified live against both.
        const string Body = """{"admin":{"discord":{"channels":[],"webhooks":["teamharmonyrares"],"users":false}}}""";

        var v2 = ScriptedHandler.Ok(Body);
        Assert.NotNull(await CreateSut(v2, "5.2.1").GetAdminRolesAsync("user1"));
        Assert.Equal($"{ApiAddress}/api/v2/humans/user1/admin-roles", Assert.Single(v2.Requests).Url);

        var v1 = ScriptedHandler.Ok(Body);
        Assert.NotNull(await CreateSut(v1, "5.1.0").GetAdminRolesAsync("user1"));
        Assert.Equal(
            $"{ApiAddress}/api/humans/user1/getAdministrationRoles", Assert.Single(v1.Requests).Url);
    }

    [Fact]
    public async Task AdminRolesTellsAMissingHumanApartFromAServerThatCouldNotAnswer()
    {
        // The whole point of this method's contract. Answering null for both -- which it did for every
        // non-2xx -- told UserRoleResolver "administers nothing" confidently enough to cache, denying a
        // legitimate delegate for the full minute after a blip. See #656 and #667.
        var missing = ScriptedHandler.Problem(
            HttpStatusCode.NotFound, """{"title":"Not Found","status":404,"detail":"human not found"}""");
        Assert.Null(await CreateSut(missing, "5.2.1").GetAdminRolesAsync("nobody"));

        var degraded = new ScriptedHandler(new Reply(HttpStatusCode.ServiceUnavailable, "{}"));
        await Assert.ThrowsAsync<HttpRequestException>(
            () => CreateSut(degraded, "5.2.1").GetAdminRolesAsync("user1"));
    }

    [Fact]
    public async Task AWebhookIdIsEncodedIntoTheAdminRolesPath()
    {
        // A webhook human's id is a URL. The v1 call did not encode it, so its slashes became extra path
        // segments and the request could only 404 -- which then read as "administers nothing".
        var handler = ScriptedHandler.Ok("""{"admin":{"discord":{"channels":[],"webhooks":[],"users":false}}}""");

        await CreateSut(handler, "5.2.1").GetAdminRolesAsync("https://discordapp.com/api/webhooks/1/tok");

        Assert.Equal(
            $"{ApiAddress}/api/v2/humans/https%3A%2F%2Fdiscordapp.com%2Fapi%2Fwebhooks%2F1%2Ftok/admin-roles",
            Assert.Single(handler.Requests).Url);
    }

    [Fact]
    public async Task AnUnreachableServerStaysOnV1()
    {
        // Unknown version means unknown routes. Guessing v2 costs an extra round-trip on every call
        // while PoracleNG is down, and it is down often enough for that to matter.
        var handler = ScriptedHandler.Ok("""{"human":{"id":"user1"},"status":"ok"}""");
        var sut = CreateSut(handler, version: null);

        await sut.GetHumanAsync("user1");

        Assert.Equal($"{ApiAddress}/api/humans/one/user1", Assert.Single(handler.Requests).Url);
    }

    // ---- profiles on v2 (#836, #837) ------------------------------------------------------------
    //
    // Both gate on what the server's own /openapi.json declares rather than on the route answering,
    // because on 5.2.1 both routes exist and neither does what is wanted: the create answers
    // {"status":"ok"} with no number, and the PATCH declares active_hours alone under
    // additionalProperties:false, so sending a name to it is a 422.

    private static PoracleV2Capabilities Carrying(bool create = false, bool rename = false) =>
        new() { Read = true, ProfileCreateReturnsNumber = create, ProfileRename = rename };

    [Fact]
    public async Task CreatingAProfileTakesTheNumberTheServerAssigned()
    {
        var handler = ScriptedHandler.Ok("""
            {"profile_no":2,"profile":{"uid":347,"id":"user1","profile_no":2,"name":"probe-two"}}
            """);
        var sut = CreateSut(handler, version: "5.3.0", capabilities: Carrying(create: true));

        var assigned = await sut.AddProfileAsync("user1", Body("""{"name":"probe-two"}"""));

        Assert.Equal(2, assigned);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal($"{ApiAddress}/api/v2/humans/user1/profiles", request.Url);
    }

    [Fact]
    public async Task CreatingAProfileStaysOnV1WhenTheServerWouldNotReportTheNumber()
    {
        // The no-change case, and the reason this gates on the schema rather than on the route. 5.2.1
        // serves POST /v2/humans/{id}/profiles perfectly well — it just answers {"status":"ok"}, so
        // going there would cost a round trip and still leave the caller diffing the profile list.
        var handler = ScriptedHandler.Ok("""{"status":"ok"}""");
        var sut = CreateSut(handler, version: "5.2.1", capabilities: Carrying(create: false));

        var assigned = await sut.AddProfileAsync("user1", Body("""{"name":"probe-two"}"""));

        Assert.Null(assigned);
        Assert.Equal($"{ApiAddress}/api/profiles/user1/add", Assert.Single(handler.Requests).Url);
    }

    [Fact]
    public async Task ACreateThatAnswersWithoutTheNumberIsNotAFailure()
    {
        // A server that declares the capability and then does not carry it. Null sends the caller back
        // to diffing the list, which is what it does on every released version anyway.
        var handler = ScriptedHandler.Ok("""{"status":"ok"}""");
        var sut = CreateSut(handler, version: "5.3.0", capabilities: Carrying(create: true));

        Assert.Null(await sut.AddProfileAsync("user1", Body("""{"name":"probe-two"}""")));
    }

    [Fact]
    public async Task RenamingAProfilePatchesV2AndSaysTheNameWasApplied()
    {
        var handler = ScriptedHandler.Ok("""{"status":"ok"}""");
        var sut = CreateSut(handler, version: "5.3.0", capabilities: Carrying(rename: true));

        var applied = await sut.UpdateProfileAsync(
            "user1", Body("""{"profile_no":2,"name":"renamed","active_hours":null}"""));

        Assert.True(applied);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Patch, request.Method);
        Assert.Equal($"{ApiAddress}/api/v2/humans/user1/profiles/2", request.Url);

        // profile_no addresses the row in the path; v2 sets additionalProperties:false, so leaving it in
        // the body is a 422 rather than a harmless extra.
        using var body = JsonDocument.Parse(request.Body!);
        Assert.False(body.RootElement.TryGetProperty("profile_no", out _));
        Assert.Equal("renamed", body.RootElement.GetProperty("name").GetString());

        // v1 says "leave this alone" with a null; v2 says it by omission and declares active_hours as an
        // array rather than a nullable one. A live build accepts the null anyway, but that is tolerance
        // the schema does not promise and TryV2Async falls back on a missing route, not on a 422.
        Assert.False(body.RootElement.TryGetProperty("active_hours", out _));
    }

    [Fact]
    public async Task ClearingTheScheduleIsAnEmptyArrayAndSurvivesTheNullStrip()
    {
        // The legitimate-case half: [] is how v2 documents "clear it", and it is not a null.
        var handler = ScriptedHandler.Ok("""{"status":"ok"}""");
        var sut = CreateSut(handler, version: "5.3.0", capabilities: Carrying(rename: true));

        await sut.UpdateProfileAsync("user1", Body("""{"profile_no":2,"active_hours":[]}"""));

        using var body = JsonDocument.Parse(Assert.Single(handler.Requests).Body!);
        Assert.Equal(JsonValueKind.Array, body.RootElement.GetProperty("active_hours").ValueKind);
        Assert.Equal(0, body.RootElement.GetProperty("active_hours").GetArrayLength());
    }

    [Fact]
    public async Task RenamingStaysOnV1WhenTheServerWouldRefuseAName()
    {
        // The load-bearing half. PATCH exists on 5.2.1 and would answer 422 for the name, so this is not
        // an optimisation — without the gate, every profile edit on the version everyone runs would fail.
        var handler = ScriptedHandler.Ok("""{"status":"ok"}""");
        var sut = CreateSut(handler, version: "5.2.1", capabilities: Carrying(rename: false));

        var applied = await sut.UpdateProfileAsync(
            "user1", Body("""{"profile_no":2,"name":"renamed"}"""));

        Assert.False(applied);
        Assert.Equal($"{ApiAddress}/api/profiles/user1/update", Assert.Single(handler.Requests).Url);
    }

    [Fact]
    public async Task AnUpdateWithNoProfileNumberStaysOnV1()
    {
        // v2 addresses the profile in the path, so a body that does not say which profile cannot go
        // there. v1 reads it out of the body and is the only surface that can serve this.
        var handler = ScriptedHandler.Ok("""{"status":"ok"}""");
        var sut = CreateSut(handler, version: "5.3.0", capabilities: Carrying(rename: true));

        Assert.False(await sut.UpdateProfileAsync("user1", Body("""{"name":"renamed"}""")));
        Assert.Equal($"{ApiAddress}/api/profiles/user1/update", Assert.Single(handler.Requests).Url);
    }

    // ---- admin human list & delete (#839) -----------------------------------------------------
    //
    // Gated purely on IPoracleV2SchemaService.AdminHumanRoutes, not on ServerCarriesV2Async/TryV2Async:
    // the schema check already encodes "the route exists AND the list item has the three #230 fields",
    // which is the exact bar HumanService needs before it can stop falling back to IHumanRepository.

    private static PoracleV2Capabilities AdminRoutes(bool carries) => new() { Read = true, AdminHumanRoutes = carries };

    [Fact]
    public async Task ListHumansReturnsNullWithoutTheCapabilitySoTheCallerFallsBackToTheRepository()
    {
        var handler = ScriptedHandler.Ok("""{"humans":[]}""");
        var sut = CreateSut(handler, version: "5.2.1", capabilities: AdminRoutes(carries: false));

        Assert.Null(await sut.ListHumansAsync());
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ListHumansReadsTheWrapperAndTheBooleanFieldsTheListEndpointActuallySends()
    {
        // enabled/admin_disable come back as real JSON booleans here, where GET /api/v2/humans/{id}
        // sends the v1-style 0/1 for the same fields on the same server -- verified live against develop.
        var handler = ScriptedHandler.Ok("""
            {"humans":[{"id":"user1","type":"discord:user","name":"Ash","enabled":true,
              "admin_disable":false,"language":"en","current_profile_no":2,
              "last_checked":"2026-10-01T00:00:00Z","disabled_date":null,"notes":"vip"}]}
            """);
        var sut = CreateSut(handler, version: "5.2.1", capabilities: AdminRoutes(carries: true));

        var humans = await sut.ListHumansAsync();

        Assert.Equal($"{ApiAddress}/api/v2/humans", Assert.Single(handler.Requests).Url);
        var human = Assert.Single(humans!);
        Assert.Equal("user1", human.Id);
        Assert.Equal(1, human.Enabled);
        Assert.Equal(0, human.AdminDisable);
        Assert.Equal(2, human.CurrentProfileNo);
        Assert.Equal("vip", human.Notes);
        Assert.Null(human.DisabledDate);
    }

    [Fact]
    public async Task ListHumansFiltersByTypeAndEncodesTheCommaSeparatedIds()
    {
        var handler = ScriptedHandler.Ok("""{"humans":[]}""");
        var sut = CreateSut(handler, version: "5.2.1", capabilities: AdminRoutes(carries: true));

        await sut.ListHumansAsync(type: "webhook", ids: ["user1", "https://discordapp.com/api/webhooks/1/tok"]);

        var expected = $"{ApiAddress}/api/v2/humans?type=webhook&id=user1,https%3A%2F%2Fdiscordapp.com%2Fapi%2Fwebhooks%2F1%2Ftok";
        Assert.Equal(expected, Assert.Single(handler.Requests).Url);
    }

    [Fact]
    public async Task DeleteHumanReturnsNullWithoutTheCapabilitySoTheCallerFallsBackToTheRepository()
    {
        var handler = ScriptedHandler.Ok("""{"status":"ok"}""");
        var sut = CreateSut(handler, version: "5.2.1", capabilities: AdminRoutes(carries: false));

        Assert.Null(await sut.DeleteHumanAsync("user1"));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task DeleteHumanReturnsTrueOnSuccess()
    {
        var handler = ScriptedHandler.Ok("""{"status":"ok"}""");
        var sut = CreateSut(handler, version: "5.2.1", capabilities: AdminRoutes(carries: true));

        Assert.True(await sut.DeleteHumanAsync("user1"));
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.Equal($"{ApiAddress}/api/v2/humans/user1", request.Url);
    }

    [Fact]
    public async Task DeleteHumanReturnsFalseRatherThanThrowingWhenTheAccountIsAlreadyGone()
    {
        // false, not an exception: the account being gone is the caller's success case, not a refusal.
        var handler = ScriptedHandler.Problem(
            HttpStatusCode.NotFound, """{"title":"Not Found","status":404,"detail":"human not found"}""");
        var sut = CreateSut(handler, version: "5.2.1", capabilities: AdminRoutes(carries: true));

        Assert.False(await sut.DeleteHumanAsync("nobody"));
    }

    [Fact]
    public async Task DeleteHumanThrowsOnAGenuineRefusal()
    {
        var handler = ScriptedHandler.Problem(
            HttpStatusCode.UnprocessableEntity, """{"title":"Unprocessable Entity","status":422,"detail":"cannot delete"}""");
        var sut = CreateSut(handler, version: "5.2.1", capabilities: AdminRoutes(carries: true));

        var refused = await Assert.ThrowsAsync<PoracleRequestRefusedException>(
            () => sut.DeleteHumanAsync("user1"));

        Assert.Contains("cannot delete", refused.Message, StringComparison.Ordinal);
    }

    // ---- trusted setAreas for the active-profile dual writer (#838) ----------------------------
    //
    // Gated on BOTH IPoracleV2SchemaService.TrustedSetAreas (the schema declares the property) AND
    // IAreaSecurityPolicyService.IsConfirmedDisabledAsync (area_security is confirmed off) -- neither
    // /openapi.json nor /health distinguishes a pre-jfberry/PoracleNG#230 server from a post-#230 one,
    // verified live against two builds either side of the fix, so the schema alone cannot say whether
    // trusted's community-restriction bypass is safe to rely on.

    private static PoracleV2Capabilities TrustedAreas(bool carries) => new() { Read = true, TrustedSetAreas = carries };

    [Fact]
    public async Task AddAreaTrustedReturnsNullWithoutTheSchemaCapability()
    {
        var handler = ScriptedHandler.Ok("""{"human":{"id":"user1","area":"[\"downtown\"]"}}""");
        var sut = CreateSut(
            handler, version: "5.2.1", capabilities: TrustedAreas(carries: false), areaSecurityConfirmedDisabled: true);

        Assert.Null(await sut.AddAreaToActiveProfileTrustedAsync("user1", "park"));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task AddAreaTrustedReturnsNullWhenAreaSecurityIsNotConfirmedDisabled()
    {
        // The load-bearing half: the schema can carry trusted on a pre-#230 build too, where sending
        // it would silently bypass a community's allowed-area restriction if area_security is on.
        var handler = ScriptedHandler.Ok("""{"human":{"id":"user1","area":"[\"downtown\"]"}}""");
        var sut = CreateSut(
            handler, version: "5.2.1", capabilities: TrustedAreas(carries: true), areaSecurityConfirmedDisabled: false);

        Assert.Null(await sut.AddAreaToActiveProfileTrustedAsync("user1", "park"));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task AddAreaTrustedPostsTheFullListWithTrustedTrue()
    {
        var handler = new ScriptedHandler(
            new Reply(HttpStatusCode.OK, """{"human":{"id":"user1","area":"[\"downtown\"]"}}"""),
            new Reply(HttpStatusCode.OK, """{"areas":["downtown","park"],"rejected":[]}"""));
        var sut = CreateSut(
            handler, version: "5.2.1", capabilities: TrustedAreas(carries: true), areaSecurityConfirmedDisabled: true);

        Assert.True(await sut.AddAreaToActiveProfileTrustedAsync("user1", "park"));

        Assert.Equal(2, handler.Requests.Count);
        var post = handler.Requests[1];
        Assert.Equal(HttpMethod.Post, post.Method);
        Assert.Equal($"{ApiAddress}/api/v2/humans/user1/areas", post.Url);
        var body = JsonDocument.Parse(post.Body!).RootElement;
        Assert.True(body.GetProperty("trusted").GetBoolean());
        Assert.Equal(
            ["downtown", "park"],
            body.GetProperty("areas").EnumerateArray().Select(e => e.GetString()).ToList());
    }

    [Fact]
    public async Task AddAreaTrustedIsANoOpWhenAlreadyPresent()
    {
        var handler = ScriptedHandler.Ok("""{"human":{"id":"user1","area":"[\"downtown\"]"}}""");
        var sut = CreateSut(
            handler, version: "5.2.1", capabilities: TrustedAreas(carries: true), areaSecurityConfirmedDisabled: true);

        Assert.False(await sut.AddAreaToActiveProfileTrustedAsync("user1", "downtown"));
        Assert.Single(handler.Requests); // only the read -- no POST for a no-op
    }

    [Fact]
    public async Task RemoveAreaTrustedPostsTheReducedList()
    {
        var handler = new ScriptedHandler(
            new Reply(HttpStatusCode.OK, """{"human":{"id":"user1","area":"[\"downtown\",\"park\"]"}}"""),
            new Reply(HttpStatusCode.OK, """{"areas":["downtown"],"rejected":[]}"""));
        var sut = CreateSut(
            handler, version: "5.2.1", capabilities: TrustedAreas(carries: true), areaSecurityConfirmedDisabled: true);

        Assert.True(await sut.RemoveAreaFromActiveProfileTrustedAsync("user1", "park"));

        Assert.Equal(
            ["downtown"],
            JsonDocument.Parse(handler.Requests[1].Body!).RootElement
                .GetProperty("areas").EnumerateArray().Select(e => e.GetString()).ToList());
    }

    [Fact]
    public async Task RemoveAreaTrustedIsANoOpWhenNotPresent()
    {
        var handler = ScriptedHandler.Ok("""{"human":{"id":"user1","area":"[\"downtown\"]"}}""");
        var sut = CreateSut(
            handler, version: "5.2.1", capabilities: TrustedAreas(carries: true), areaSecurityConfirmedDisabled: true);

        Assert.False(await sut.RemoveAreaFromActiveProfileTrustedAsync("user1", "park"));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task AddAreasTrustedDedupesCaseInsensitivelyAndKeepsWhatIsAlreadyPresent()
    {
        var handler = new ScriptedHandler(
            new Reply(HttpStatusCode.OK, """{"human":{"id":"user1","area":"[\"downtown\"]"}}"""),
            new Reply(HttpStatusCode.OK, """{"areas":["downtown","park","square"],"rejected":[]}"""));
        var sut = CreateSut(
            handler, version: "5.2.1", capabilities: TrustedAreas(carries: true), areaSecurityConfirmedDisabled: true);

        Assert.True(await sut.AddAreasToActiveProfileTrustedAsync("user1", ["Park", "park", "Square", "downtown"]));

        Assert.Equal(
            ["downtown", "park", "square"],
            JsonDocument.Parse(handler.Requests[1].Body!).RootElement
                .GetProperty("areas").EnumerateArray().Select(e => e.GetString()).ToList());
    }

    [Fact]
    public async Task TrustedSetAreasReturnsNullWhenTheCurrentListCannotBeRead()
    {
        // Account gone or unreachable -- GetHumanAsync answers null, so there is nothing safe to
        // compute a full replacement from. The caller must fall back to IUserAreaDualWriter rather
        // than risk posting an incomplete list that drops areas this call never asked to touch.
        var handler = ScriptedHandler.Problem(
            HttpStatusCode.NotFound, """{"title":"Not Found","status":404,"detail":"human not found"}""");
        var sut = CreateSut(
            handler, version: "5.2.1", capabilities: TrustedAreas(carries: true), areaSecurityConfirmedDisabled: true);

        Assert.Null(await sut.AddAreaToActiveProfileTrustedAsync("nobody", "park"));
    }

    [Fact]
    public async Task TrustedSetAreasSucceedsEvenWhenSomethingUnexpectedIsRejected()
    {
        // Logged, not thrown. Every HACK: trusted-set-areas call site this replaces already treats
        // its own write as best-effort, and the geofence row that produced this name is the source
        // of truth either way.
        var handler = new ScriptedHandler(
            new Reply(HttpStatusCode.OK, """{"human":{"id":"user1","area":"[\"downtown\"]"}}"""),
            new Reply(HttpStatusCode.OK, """{"areas":["downtown"],"rejected":["park"]}"""));
        var sut = CreateSut(
            handler, version: "5.2.1", capabilities: TrustedAreas(carries: true), areaSecurityConfirmedDisabled: true);

        Assert.True(await sut.AddAreaToActiveProfileTrustedAsync("user1", "park"));
    }

    // ---- what the controllers actually send ------------------------------------------------------
    //
    // Every test above posts {"name":"probe-two"}, a body no caller builds. The four callers of
    // AddProfileAsync send area, latitude and longitude as well, and every caller sends active_hours as the
    // JSON string it is stored as. Against a server carrying PoracleNG #217 that made every profile create,
    // duplicate and import a 422, and every rename too -- all green here, all failing on a running build.
    // These post the real shapes and check the result against the schema the server publishes.

    private const string ControllerCreateBody = """
        {"name":"probe-two","area":"[\"aberdeen\"]","latitude":41.65,"longitude":-83.53,
         "active_hours":"[{\"day\":1,\"hours\":\"8\",\"mins\":0},{\"day\":7,\"hours\":22,\"mins\":\"30\"}]"}
        """;

    [Fact]
    public async Task CreatingAProfileSendsOnlyWhatV2AddProfileBodyDeclares()
    {
        var handler = ScriptedHandler.Ok("""{"profile_no":2,"profile":{"profile_no":2,"name":"probe-two"}}""");
        var sut = CreateSut(handler, version: "5.3.0", capabilities: Carrying(create: true));

        Assert.Equal(2, await sut.AddProfileAsync("user1", Body(ControllerCreateBody)));

        var request = Assert.Single(handler.Requests);
        Assert.Equal($"{ApiAddress}/api/v2/humans/user1/profiles", request.Url);
        using var body = JsonDocument.Parse(request.Body!);
        AssertConformsTo(body.RootElement, "V2AddProfileBody");
        Assert.Equal("probe-two", body.RootElement.GetProperty("name").GetString());

        // PoracleNG stores hours and mins as strings as often as numbers; v2 declares integers.
        var entries = body.RootElement.GetProperty("active_hours").EnumerateArray().ToList();
        Assert.Equal(2, entries.Count);
        Assert.Equal(8, entries[0].GetProperty("hours").GetInt32());
        Assert.Equal(30, entries[1].GetProperty("mins").GetInt32());
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"{}\"")]
    [InlineData("\"\"")]
    public async Task CreatingAProfileWithNoScheduleOmitsIt(string activeHours)
    {
        // "{}" is what PoracleNG writes for a profile with no schedule. It is not an entry list.
        var handler = ScriptedHandler.Ok("""{"profile_no":2}""");
        var sut = CreateSut(handler, version: "5.3.0", capabilities: Carrying(create: true));

        await sut.AddProfileAsync("user1", Body($$"""{"name":"probe-two","area":"[]","latitude":0,"longitude":0,"active_hours":{{activeHours}}}"""));

        using var body = JsonDocument.Parse(Assert.Single(handler.Requests).Body!);
        Assert.False(body.RootElement.TryGetProperty("active_hours", out _));
        AssertConformsTo(body.RootElement, "V2AddProfileBody");
    }

    [Theory]
    [InlineData("[{\\\"day\\\":8,\\\"hours\\\":8,\\\"mins\\\":0}]")]
    [InlineData("[{\\\"day\\\":1,\\\"hours\\\":8}]")]
    [InlineData("[{\\\"day\\\":1,\\\"hours\\\":8,\\\"mins\\\":0,\\\"colour\\\":\\\"red\\\"}]")]
    [InlineData("not json")]
    public async Task CreatingAProfileWithAScheduleV2CannotTakeStaysOnV1(string activeHours)
    {
        // Never reshape what PoracleNG will accept. v1 has taken these for years; v2 would 422 them.
        var handler = ScriptedHandler.Ok("""{"status":"ok"}""");
        var sut = CreateSut(handler, version: "5.3.0", capabilities: Carrying(create: true));

        Assert.Null(await sut.AddProfileAsync("user1", Body($$"""{"name":"probe-two","active_hours":"{{activeHours}}"}""")));
        Assert.Equal($"{ApiAddress}/api/profiles/user1/add", Assert.Single(handler.Requests).Url);
    }

    [Fact]
    public async Task RenamingSendsTheStoredScheduleAsAnArray()
    {
        // ProfileController.Update always sends active_hours, falling back to the stored string, so a
        // plain rename carries it too.
        var handler = ScriptedHandler.Ok("""{"status":"ok"}""");
        var sut = CreateSut(handler, version: "5.3.0", capabilities: Carrying(rename: true));

        Assert.True(await sut.UpdateProfileAsync(
            "user1", Body("""{"profile_no":2,"name":"renamed","active_hours":"[{\"day\":1,\"hours\":8,\"mins\":0}]"}""")));

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Patch, request.Method);
        using var body = JsonDocument.Parse(request.Body!);
        AssertConformsTo(body.RootElement, "V2UpdateProfileBody");
        Assert.Equal(1, body.RootElement.GetProperty("active_hours")[0].GetProperty("day").GetInt32());
    }

    /// <summary>
    /// Changed deliberately. This used to assert that "{}" was omitted from the PATCH, which encoded the
    /// defect rather than guarding against one: v2 reads an omitted field as "leave it unchanged", so a
    /// caller sending "" to clear a schedule kept the old one. On an update every spelling of "no schedule"
    /// is sent as the empty list. A profile that never had a schedule is cleared to the nothing it already
    /// had, which is harmless.
    /// </summary>
    [Theory]
    [InlineData("\"{}\"")]
    [InlineData("\"\"")]
    [InlineData("\"[]\"")]
    [InlineData("[]")]
    public async Task AnUpdateSayingNoScheduleClearsIt(string activeHours)
    {
        var handler = ScriptedHandler.Ok("""{"status":"ok"}""");
        var sut = CreateSut(handler, version: "5.3.0", capabilities: Carrying(rename: true));

        Assert.True(await sut.UpdateProfileAsync(
            "user1", Body($$"""{"profile_no":2,"name":"renamed","active_hours":{{activeHours}}}""")));

        using var body = JsonDocument.Parse(Assert.Single(handler.Requests).Body!);
        Assert.Equal("renamed", body.RootElement.GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Array, body.RootElement.GetProperty("active_hours").ValueKind);
        Assert.Equal(0, body.RootElement.GetProperty("active_hours").GetArrayLength());
        AssertConformsTo(body.RootElement, "V2UpdateProfileBody");
    }

    [Fact]
    public async Task AnUpdateWithANullScheduleStillLeavesItAlone()
    {
        // The half that must not move: null is "not part of this edit", and v2 says that by omission.
        var handler = ScriptedHandler.Ok("""{"status":"ok"}""");
        var sut = CreateSut(handler, version: "5.3.0", capabilities: Carrying(rename: true));

        await sut.UpdateProfileAsync("user1", Body("""{"profile_no":2,"name":"renamed","active_hours":null}"""));

        using var body = JsonDocument.Parse(Assert.Single(handler.Requests).Body!);
        Assert.False(body.RootElement.TryGetProperty("active_hours", out _));
    }

    [Fact]
    public async Task RenamingWithAScheduleV2CannotTakeStaysOnV1()
    {
        // False sends ProfileController to its direct rename, so the name still lands.
        var handler = ScriptedHandler.Ok("""{"status":"ok"}""");
        var sut = CreateSut(handler, version: "5.3.0", capabilities: Carrying(rename: true));

        Assert.False(await sut.UpdateProfileAsync(
            "user1", Body("""{"profile_no":2,"name":"renamed","active_hours":"[{\"day\":0,\"hours\":8,\"mins\":0}]"}""")));
        Assert.Equal($"{ApiAddress}/api/profiles/user1/update", Assert.Single(handler.Requests).Url);
    }

    /// <summary>
    /// Every property of <paramref name="body"/> is one the named schema declares, recursing into
    /// <c>active_hours</c> entries -- the schemas set <c>additionalProperties:false</c>, so anything else
    /// is a 422. Read from the fixture captured off a running build rather than restated here.
    /// </summary>
    private static void AssertConformsTo(JsonElement body, string schema)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(OpenApiFixture("next")));
        var schemas = doc.RootElement.GetProperty("components").GetProperty("schemas");
        var declared = schemas.GetProperty(schema).GetProperty("properties").EnumerateObject().Select(p => p.Name).ToHashSet();
        var entry = schemas.GetProperty("V2ActiveHourEntry").GetProperty("properties").EnumerateObject().Select(p => p.Name).ToHashSet();

        foreach (var property in body.EnumerateObject())
        {
            Assert.Contains(property.Name, declared);
        }

        if (body.TryGetProperty("active_hours", out var hours))
        {
            Assert.Equal(JsonValueKind.Array, hours.ValueKind);
            foreach (var item in hours.EnumerateArray())
            {
                foreach (var property in item.EnumerateObject())
                {
                    Assert.Contains(property.Name, entry);
                    Assert.Equal(JsonValueKind.Number, property.Value.ValueKind);
                }
            }
        }
    }

    private static string OpenApiFixture(string name, [System.Runtime.CompilerServices.CallerFilePath] string? here = null) =>
        Path.Combine(Path.GetDirectoryName(here)!, "..", "Fixtures", $"poracleng-openapi-{name}.json");

    private static JsonElement Body(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static PoracleHumanProxy CreateSut(
        ScriptedHandler handler,
        string? version,
        IMemoryCache? cache = null,
        PoracleV2Capabilities? capabilities = null,
        bool areaSecurityConfirmedDisabled = false)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Poracle:ApiAddress"] = ApiAddress,
                ["Poracle:ApiSecret"] = "test-secret",
            })
            .Build();

        return new PoracleHumanProxy(
            new HttpClient(handler),
            config,
            PoracleHumanProxyTests.ServerProfile(version),
            PoracleHumanProxyTests.V2Schema(capabilities),
            PoracleHumanProxyTests.AreaSecurityPolicy(areaSecurityConfirmedDisabled),
            cache ?? new MemoryCache(new MemoryCacheOptions()),
            Mock.Of<ILogger<PoracleHumanProxy>>());
    }

    private sealed record Reply(HttpStatusCode Status, string Body, string ContentType = "application/json");

    private sealed record Sent(HttpMethod Method, string Url, string? Body);

    private sealed class ScriptedHandler(params Reply[] replies) : HttpMessageHandler
    {
        private int _next;

        public List<Sent> Requests { get; } = [];

        public static ScriptedHandler Ok(string body) => new(new Reply(HttpStatusCode.OK, body));

        public static ScriptedHandler Problem(HttpStatusCode status, string body) =>
            new(new Reply(status, body, "application/problem+json"));

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            this.Requests.Add(new Sent(request.Method, request.RequestUri!.ToString(), body));

            var reply = replies[Math.Min(this._next++, replies.Length - 1)];
            return new HttpResponseMessage(reply.Status)
            {
                Content = new StringContent(reply.Body, Encoding.UTF8, reply.ContentType),
            };
        }
    }
}
