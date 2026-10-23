using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// The human, location and place operations on PoracleNG's <c>/api/v2</c> surface, and every way they
/// answer when the server is older than the 5.4.0 minimum.
/// </summary>
/// <remarks>
/// <para>
/// Every response here was taken from a live 5.2.1 and, for the fallbacks, a live 5.1.0: the
/// <c>{"human":{...}}</c> wrapper that both versions share, the 409 on a duplicate place label, the 404
/// problem+json for a place that does not exist, and gin's plaintext <c>404 page not found</c> for a
/// route the build has never had.
/// </para>
/// <para>
/// There is no v1 fallback: a route the server does not have is a server older than 5.4.0, and says so.
/// </para>
/// </remarks>
public class PoracleHumanProxyV2Tests
{
    private const string ApiAddress = "http://localhost:3030";

    /// <summary>What gin answers for a route this build does not carry. Plaintext, not problem+json.</summary>
    private const string RouteMissing = "404 page not found";

    [Fact]
    public async Task GetHumanReadsTheV2Route()
    {
        var handler = ScriptedHandler.Ok("""{"human":{"id":"user1","language":"en"}}""");
        var sut = CreateSut(handler);

        var human = await sut.GetHumanAsync("user1");

        var request = Assert.Single(handler.Requests);
        Assert.Equal($"{ApiAddress}/api/v2/humans/user1", request.Url);
        Assert.Equal("user1", human!.Value.GetProperty("id").GetString());
    }

    [Fact]
    public async Task AMissingHumanIsNotMistakenForAMissingRoute()
    {
        // Both are 404. Only the content type separates them, and reading the problem+json one as a
        // missing route would drop the whole surface to v1 for five minutes over a stale user id.
        var handler = ScriptedHandler.Problem(
            HttpStatusCode.NotFound, """{"title":"Not Found","status":404,"detail":"human not found"}""");
        var sut = CreateSut(handler);

        Assert.Null(await sut.GetHumanAsync("nobody"));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task AServerThatCannotAnswerIsNotMistakenForAMissingHuman()
    {
        // The purge read this as "already gone" and answered 404 to an admin mid-outage. A 503 is neither
        // a missing human nor a missing route, so it neither answers null nor retries on v1. See #994.
        var handler = ScriptedHandler.Problem(
            HttpStatusCode.ServiceUnavailable, """{"title":"Service Unavailable","status":503,"detail":"database error"}""");
        var sut = CreateSut(handler);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => sut.GetHumanAsync("user1"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task SetLanguagePostsTheLanguageToTheV2Route()
    {
        var v2 = ScriptedHandler.Ok("""{"status":"ok"}""");
        await CreateSut(v2).SetLanguageAsync("user1", "de");

        var sent = Assert.Single(v2.Requests);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal($"{ApiAddress}/api/v2/humans/user1/language", sent.Url);
        Assert.Equal("de", JsonDocument.Parse(sent.Body!).RootElement.GetProperty("language").GetString());
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
            () => CreateSut(handler).SetLanguageAsync("user1", ""));

        Assert.Contains("language is required", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AdminDisableSendsDisabledNotState()
    {
        // v2's body is `disabled`; v1's was `state`, which v2 refuses with a 422.
        var v2 = ScriptedHandler.Ok("""{"status":"ok"}""");
        await CreateSut(v2).AdminDisabledAsync("user1", true);

        var sent = Assert.Single(v2.Requests);
        Assert.Equal($"{ApiAddress}/api/v2/humans/user1/admin-disable", sent.Url);
        var v2Body = JsonDocument.Parse(sent.Body!).RootElement;
        Assert.True(v2Body.GetProperty("disabled").GetBoolean());
        Assert.False(v2Body.TryGetProperty("state", out _));
    }

    [Fact]
    public async Task SetLocationMovesTheCoordinatesIntoTheBody()
    {
        var handler = ScriptedHandler.Ok("""{"status":"ok"}""");
        await CreateSut(handler).SetLocationAsync("user1", 51.5, -0.12);

        var sent = Assert.Single(handler.Requests);
        Assert.Equal($"{ApiAddress}/api/v2/humans/user1/location", sent.Url);
        var body = JsonDocument.Parse(sent.Body!).RootElement;
        Assert.Equal(51.5, body.GetProperty("latitude").GetDouble());
        Assert.Equal(-0.12, body.GetProperty("longitude").GetDouble());
        Assert.False(body.TryGetProperty("lat", out _));
        Assert.False(body.TryGetProperty("lon", out _));
    }

    [Fact]
    public async Task SwitchProfileMovesTheNumberIntoTheBody()
    {
        var handler = ScriptedHandler.Ok("""{"status":"ok"}""");
        await CreateSut(handler).SwitchProfileAsync("user1", 3);

        var sent = Assert.Single(handler.Requests);
        Assert.Equal($"{ApiAddress}/api/v2/humans/user1/profile", sent.Url);
        Assert.Equal(3, JsonDocument.Parse(sent.Body!).RootElement.GetProperty("profile_no").GetInt32());
    }

    [Fact]
    public async Task AddPlaceSendsLatitudeLongitude()
    {
        var handler = ScriptedHandler.Ok("""{"status":"ok"}""");
        var sut = CreateSut(handler);

        var refusal = await sut.AddPlaceAsync(
            "user1", new SavedPlace { Label = "home", Latitude = 1.5, Longitude = 2.5 });

        Assert.Null(refusal);
        var sent = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal($"{ApiAddress}/api/v2/humans/user1/locations", sent.Url);
        var body = JsonDocument.Parse(sent.Body!).RootElement;
        Assert.Equal("home", body.GetProperty("label").GetString());
        Assert.Equal(1.5, body.GetProperty("latitude").GetDouble());
        Assert.Equal(2.5, body.GetProperty("longitude").GetDouble());
        Assert.False(body.TryGetProperty("lat", out _));
    }

    [Fact]
    public async Task ADuplicateLabelIsReportedNotThrown()
    {
        // v2 answers a real 409 where v1 buried the same refusal in a 200 body. Both have to reach the
        // caller as the same string, because the controller turns it into the message shown by the field.
        var handler = ScriptedHandler.Problem(
            HttpStatusCode.Conflict,
            """{"title":"Conflict","status":409,"detail":"location label already exists"}""");
        var sut = CreateSut(handler);

        var refusal = await sut.AddPlaceAsync("user1", new SavedPlace { Label = "home" });

        Assert.Equal("location label already exists", refusal);
    }

    [Fact]
    public async Task ALabelTooLongForTheColumnIsRefusedBeforeItIsSent()
    {
        // user_locations.label is varchar(64). v1 reported the overflow as "Data too long for column
        // 'label'" inside a 200 and v2 answers 500 {"detail":"database error"} -- both verified live,
        // and neither is a sentence to show someone who typed a long name.
        var handler = ScriptedHandler.Ok("""{"status":"ok"}""");
        var sut = CreateSut(handler);

        var refusal = await sut.AddPlaceAsync("user1", new SavedPlace { Label = new string('x', 65) });

        Assert.NotNull(refusal);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ALabelOfExactlySixtyFourStillSaves()
    {
        var handler = ScriptedHandler.Ok("""{"status":"ok"}""");
        var sut = CreateSut(handler);

        Assert.Null(await sut.AddPlaceAsync("user1", new SavedPlace { Label = new string('x', 64) }));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task UpdatePlacePutsTheNewCoordinates()
    {
        var handler = ScriptedHandler.Ok("""{"status":"ok"}""");
        var sut = CreateSut(handler);

        await sut.UpdatePlaceAsync("user1", "home", 9.5, 8.5);

        var sent = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Put, sent.Method);
        Assert.Equal($"{ApiAddress}/api/v2/humans/user1/locations/home", sent.Url);
        var body = JsonDocument.Parse(sent.Body!).RootElement;
        Assert.Equal(9.5, body.GetProperty("latitude").GetDouble());
        Assert.Equal(8.5, body.GetProperty("longitude").GetDouble());
    }

    // The three location request bodies are additionalProperties:false and 5.4.0 names the coordinates
    // latitude/longitude on all of them (jfberry/PoracleNG#303); lat/lon is a 422.

    [Fact]
    public async Task SetLocationSendsLatitudeLongitudeWhereTheServerDeclaresThem()
    {
        var handler = ScriptedHandler.Ok("""{"status":"ok"}""");
        await CreateSut(handler).SetLocationAsync("user1", 51.5, -0.12);

        var sent = Assert.Single(handler.Requests);
        Assert.Equal($"{ApiAddress}/api/v2/humans/user1/location", sent.Url);
        var body = JsonDocument.Parse(sent.Body!).RootElement;
        Assert.Equal(51.5, body.GetProperty("latitude").GetDouble());
        Assert.Equal(-0.12, body.GetProperty("longitude").GetDouble());
        Assert.False(body.TryGetProperty("lat", out _));
        Assert.False(body.TryGetProperty("lon", out _));
        AssertConformsTo(body, "V2LocationBody");
    }

    [Fact]
    public async Task AddPlaceSendsLatitudeLongitudeWhereTheServerDeclaresThem()
    {
        var handler = ScriptedHandler.Ok("""{"status":"ok"}""");
        var sut = CreateSut(handler);

        Assert.Null(await sut.AddPlaceAsync(
            "user1", new SavedPlace { Label = "home", Latitude = 1.5, Longitude = 2.5 }));

        var sent = Assert.Single(handler.Requests);
        Assert.Equal($"{ApiAddress}/api/v2/humans/user1/locations", sent.Url);
        var body = JsonDocument.Parse(sent.Body!).RootElement;
        Assert.Equal("home", body.GetProperty("label").GetString());
        Assert.Equal(1.5, body.GetProperty("latitude").GetDouble());
        Assert.Equal(2.5, body.GetProperty("longitude").GetDouble());
        Assert.False(body.TryGetProperty("lat", out _));
        Assert.False(body.TryGetProperty("lon", out _));
        AssertConformsTo(body, "V2AddLocationBody");
    }

    [Fact]
    public async Task UpdatePlaceSendsLatitudeLongitudeWhereTheServerDeclaresThem()
    {
        var handler = ScriptedHandler.Ok("""{"status":"ok"}""");
        var sut = CreateSut(handler);

        await sut.UpdatePlaceAsync("user1", "home", 9.5, 8.5);

        var sent = Assert.Single(handler.Requests);
        Assert.Equal($"{ApiAddress}/api/v2/humans/user1/locations/home", sent.Url);
        var body = JsonDocument.Parse(sent.Body!).RootElement;
        Assert.Equal(9.5, body.GetProperty("latitude").GetDouble());
        Assert.Equal(8.5, body.GetProperty("longitude").GetDouble());
        Assert.False(body.TryGetProperty("lat", out _));
        Assert.False(body.TryGetProperty("lon", out _));
        AssertConformsTo(body, "V2UpdateLocationBody");
    }

    [Fact]
    public async Task UpdatePlaceOnAServerWithoutTheRouteIsUnsupported()
    {
        // The v1 server answered 501 through a false return. Now the route is the only one, and its
        // plaintext 404 names the minimum release.
        var handler = new ScriptedHandler(new Reply(HttpStatusCode.NotFound, RouteMissing, "text/plain"));
        var sut = CreateSut(handler);

        var ex = await Assert.ThrowsAsync<PoracleUnsupportedException>(
            () => sut.UpdatePlaceAsync("user1", "home", 9.5, 8.5));

        Assert.Contains("5.4.0", ex.Message, StringComparison.Ordinal);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task UpdatingAPlaceThatIsNotThereIsARefusalNotAMissingRoute()
    {
        var handler = ScriptedHandler.Problem(
            HttpStatusCode.NotFound, """{"title":"Not Found","status":404,"detail":"location not found"}""");
        var sut = CreateSut(handler);

        var refused = await Assert.ThrowsAsync<PoracleRequestRefusedException>(
            () => sut.UpdatePlaceAsync("user1", "nope", 1, 2));

        Assert.Equal(404, refused.StatusCode);
        Assert.Contains("location not found", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetPlacesReadsTheV2WrapperAndOnlyTheV2Route()
    {
        const string Body =
            """{"locations":{"default":{"latitude":1,"longitude":2},"named":[{"label":"home","latitude":3,"longitude":4}]}}""";

        var handler = ScriptedHandler.Ok(Body);
        var places = await CreateSut(handler).GetPlacesAsync("user1");

        Assert.Equal($"{ApiAddress}/api/v2/humans/user1/locations", Assert.Single(handler.Requests).Url);
        Assert.Equal("home", Assert.Single(places.Named).Label);
        Assert.Equal(1, places.Default!.Latitude);
    }

    [Fact]
    public async Task DeletePlaceSendsOnlyTheV2Delete()
    {
        var handler = ScriptedHandler.Ok("""{"status":"ok"}""");

        await CreateSut(handler).DeletePlaceAsync("user1", "home");

        var sent = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Delete, sent.Method);
        Assert.Equal($"{ApiAddress}/api/v2/humans/user1/locations/home", sent.Url);
    }

    [Theory]
    [InlineData("get")]
    [InlineData("add")]
    [InlineData("delete")]
    public async Task APlaceCallToAServerWithoutTheRouteIsUnsupportedNotRetriedOnV1(string call)
    {
        var handler = new ScriptedHandler(
            new Reply(HttpStatusCode.NotFound, RouteMissing, "text/plain"),
            new Reply(HttpStatusCode.OK, """{"locations":{},"results":[],"status":"ok"}"""));
        var sut = CreateSut(handler);

        await Assert.ThrowsAsync<PoracleUnsupportedException>(() => call switch
        {
            "get" => sut.GetPlacesAsync("user1"),
            "add" => sut.AddPlaceAsync("user1", new SavedPlace { Label = "home" }),
            _ => sut.DeletePlaceAsync("user1", "home"),
        });

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task AdminRolesReadsTheV2Route()
    {
        // The response is identical apart from v1's extra "status":"ok", because v2's handler calls the
        // same delegated-administration logic. Verified live against both.
        const string Body = """{"admin":{"discord":{"channels":[],"webhooks":["teamharmonyrares"],"users":false}}}""";

        var v2 = ScriptedHandler.Ok(Body);
        Assert.NotNull(await CreateSut(v2).GetAdminRolesAsync("user1"));
        Assert.Equal($"{ApiAddress}/api/v2/humans/user1/admin-roles", Assert.Single(v2.Requests).Url);
    }

    [Fact]
    public async Task AdminRolesTellsAMissingHumanApartFromAServerThatCouldNotAnswer()
    {
        // The whole point of this method's contract. Answering null for both -- which it did for every
        // non-2xx -- told UserRoleResolver "administers nothing" confidently enough to cache, denying a
        // legitimate delegate for the full minute after a blip. See #656 and #667.
        var missing = ScriptedHandler.Problem(
            HttpStatusCode.NotFound, """{"title":"Not Found","status":404,"detail":"human not found"}""");
        Assert.Null(await CreateSut(missing).GetAdminRolesAsync("nobody"));

        var degraded = new ScriptedHandler(new Reply(HttpStatusCode.ServiceUnavailable, "{}"));
        await Assert.ThrowsAsync<HttpRequestException>(
            () => CreateSut(degraded).GetAdminRolesAsync("user1"));
    }

    [Fact]
    public async Task AWebhookIdIsEncodedIntoTheAdminRolesPath()
    {
        // A webhook human's id is a URL. The v1 call did not encode it, so its slashes became extra path
        // segments and the request could only 404 -- which then read as "administers nothing".
        var handler = ScriptedHandler.Ok("""{"admin":{"discord":{"channels":[],"webhooks":[],"users":false}}}""");

        await CreateSut(handler).GetAdminRolesAsync("https://discordapp.com/api/webhooks/1/tok");

        Assert.Equal(
            $"{ApiAddress}/api/v2/humans/https%3A%2F%2Fdiscordapp.com%2Fapi%2Fwebhooks%2F1%2Ftok/admin-roles",
            Assert.Single(handler.Requests).Url);
    }

    // ---- admin human list & delete (#839) -----------------------------------------------------
    //
    // PoracleNG 5.4.0 is the minimum, so these routes are always there; a plaintext 404 means the server is
    // older than that and is reported as such rather than falling back to the Poracle database.

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
        var sut = CreateSut(handler);

        var humans = await sut.ListHumansAsync();

        Assert.Equal($"{ApiAddress}/api/v2/humans", Assert.Single(handler.Requests).Url);
        var human = Assert.Single(humans);
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
        var sut = CreateSut(handler);

        await sut.ListHumansAsync(type: "webhook", ids: ["user1", "https://discordapp.com/api/webhooks/1/tok"]);

        var expected = $"{ApiAddress}/api/v2/humans?type=webhook&id=user1,https%3A%2F%2Fdiscordapp.com%2Fapi%2Fwebhooks%2F1%2Ftok";
        Assert.Equal(expected, Assert.Single(handler.Requests).Url);
    }

    [Fact]
    public async Task DeleteHumanReturnsTrueOnSuccess()
    {
        var handler = ScriptedHandler.Ok("""{"status":"ok"}""");
        var sut = CreateSut(handler);

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
        var sut = CreateSut(handler);

        Assert.False(await sut.DeleteHumanAsync("nobody"));
    }

    [Fact]
    public async Task DeleteHumanThrowsOnAGenuineRefusal()
    {
        var handler = ScriptedHandler.Problem(
            HttpStatusCode.UnprocessableEntity, """{"title":"Unprocessable Entity","status":422,"detail":"cannot delete"}""");
        var sut = CreateSut(handler);

        var refused = await Assert.ThrowsAsync<PoracleRequestRefusedException>(
            () => sut.DeleteHumanAsync("user1"));

        Assert.Contains("cannot delete", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListHumansThrowsUnsupportedOnAServerWithoutTheRoute()
    {
        var handler = new ScriptedHandler(new Reply(HttpStatusCode.NotFound, RouteMissing, "text/plain"));
        var sut = CreateSut(handler);

        var ex = await Assert.ThrowsAsync<PoracleUnsupportedException>(() => sut.ListHumansAsync());

        Assert.Equal("PoracleNG 5.4.0", ex.Requires);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ListHumansAnswersAnEmptyListNotNullWhenNobodyMatches()
    {
        var handler = ScriptedHandler.Ok("""{"humans":[]}""");
        var sut = CreateSut(handler);

        Assert.Empty(await sut.ListHumansAsync(ids: ["nobody"]));
    }

    [Fact]
    public async Task DeleteHumanThrowsUnsupportedOnAServerWithoutTheRoute()
    {
        var handler = new ScriptedHandler(new Reply(HttpStatusCode.NotFound, RouteMissing, "text/plain"));
        var sut = CreateSut(handler);

        var ex = await Assert.ThrowsAsync<PoracleUnsupportedException>(() => sut.DeleteHumanAsync("user1"));

        Assert.Equal("PoracleNG 5.4.0", ex.Requires);
    }

    [Fact]
    public async Task DeleteHumanEncodesAWebhookUrlIdIntoOnePathSegment()
    {
        var handler = ScriptedHandler.Ok("""{"status":"ok"}""");
        var sut = CreateSut(handler);

        Assert.True(await sut.DeleteHumanAsync("https://discord.com/api/webhooks/1/x"));

        Assert.Equal(
            $"{ApiAddress}/api/v2/humans/https%3A%2F%2Fdiscord.com%2Fapi%2Fwebhooks%2F1%2Fx",
            Assert.Single(handler.Requests).Url);
    }

    // ---- single-human calls are v2 only (PoracleNG 5.4.0 minimum) ------------------------------------
    //
    // These ignore the server profile and the schema probe on purpose: a build too old to carry the route
    // answers gin's plaintext 404, which is the only signal, and it is reported as unsupported rather than
    // retried on a v1 path 5.4.0 no longer needs.

    private static Task Run(PoracleHumanProxy sut, string operation) => operation switch
    {
        "get" => sut.GetHumanAsync("user1"),
        "create" => sut.CreateHumanAsync(Body("""{"id":"user1","name":"n"}""")),
        "enable" => sut.StartAsync("user1"),
        "disable" => sut.StopAsync("user1"),
        "language" => sut.SetLanguageAsync("user1", "de"),
        "admin-disable" => sut.AdminDisabledAsync("user1", true),
        "location" => sut.SetLocationAsync("user1", 51.5, -0.12),
        "admin-roles" => sut.GetAdminRolesAsync("user1"),
        "list" => sut.ListHumansAsync(),
        "delete" => sut.DeleteHumanAsync("user1"),
        _ => throw new ArgumentOutOfRangeException(nameof(operation)),
    };

    public static TheoryData<string, string, string, string> SingleHumanOperations => new()
    {
        { "get", "GET", "/api/v2/humans/user1", "reading a user" },
        { "create", "POST", "/api/v2/humans", "creating a user" },
        { "enable", "POST", "/api/v2/humans/user1/enable", "enabling a user" },
        { "disable", "POST", "/api/v2/humans/user1/disable", "disabling a user" },
        { "language", "POST", "/api/v2/humans/user1/language", "setting a user's language" },
        { "admin-disable", "POST", "/api/v2/humans/user1/admin-disable", "banning a user" },
        { "location", "POST", "/api/v2/humans/user1/location", "setting a user's location" },
        { "admin-roles", "GET", "/api/v2/humans/user1/admin-roles", "reading a user's delegated webhooks" },
        { "list", "GET", "/api/v2/humans", "the admin user list" },
        { "delete", "DELETE", "/api/v2/humans/user1", "deleting a user" },
    };

    [Theory]
    [MemberData(nameof(SingleHumanOperations))]
    public async Task EachSingleHumanCallRequestsOnlyItsV2Route(string operation, string method, string path, string feature)
    {
        _ = feature;
        var handler = ScriptedHandler.Ok("""{"human":{"id":"user1"},"humans":[],"status":"ok"}""");

        // No version and no schema: neither may send a single-human call anywhere but /api/v2.
        await Run(CreateSut(handler), operation);

        var sent = Assert.Single(handler.Requests);
        Assert.Equal(new HttpMethod(method), sent.Method);
        Assert.Equal($"{ApiAddress}{path}", sent.Url);
    }

    [Theory]
    [MemberData(nameof(SingleHumanOperations))]
    public async Task APlaintextNotFoundIsAServerOlderThanTheMinimumNotAMissingHuman(
        string operation, string method, string path, string feature)
    {
        _ = method;
        _ = path;
        var handler = new ScriptedHandler(new Reply(HttpStatusCode.NotFound, RouteMissing, "text/plain"));

        var ex = await Assert.ThrowsAsync<PoracleUnsupportedException>(() => Run(CreateSut(handler), operation));

        Assert.Equal(feature, ex.Feature);
        Assert.Equal("PoracleNG 5.4.0", ex.Requires);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task AProblemJsonNotFoundIsStillAMissingHumanOnEveryCallThatReadsItThatWay()
    {
        // The legitimate case beside the refusal above: PoracleNG answering about the human is not a
        // missing route, whatever the server's version says.
        const string Missing = """{"title":"Not Found","status":404,"detail":"human not found"}""";

        Assert.Null(await CreateSut(ScriptedHandler.Problem(HttpStatusCode.NotFound, Missing)).GetHumanAsync("nobody"));
        Assert.Null(await CreateSut(ScriptedHandler.Problem(HttpStatusCode.NotFound, Missing)).GetAdminRolesAsync("nobody"));
        Assert.False(await CreateSut(ScriptedHandler.Problem(HttpStatusCode.NotFound, Missing)).DeleteHumanAsync("nobody"));
        await Assert.ThrowsAsync<AccountGoneException>(
            () => CreateSut(ScriptedHandler.Problem(HttpStatusCode.NotFound, Missing)).StartAsync("nobody"));
    }

    [Fact]
    public async Task SetLocationSendsInvariantNumbersWhateverTheServerCultureIs()
    {
        // The v1 path interpolated the doubles and produced /setLocation/51,5/-0,12 under a comma-decimal
        // culture. The v2 body is JSON, which is invariant -- pinned so it stays that way.
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            var handler = ScriptedHandler.Ok("""{"status":"ok"}""");
            await CreateSut(handler).SetLocationAsync("user1", 51.5, -0.12);

            var body = JsonDocument.Parse(Assert.Single(handler.Requests).Body!).RootElement;
            Assert.Equal(51.5, body.GetProperty("latitude").GetDouble());
            Assert.Equal(-0.12, body.GetProperty("longitude").GetDouble());
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public async Task CreateHumanSendsWhatHumanServiceBuildsAndNothingV2WouldRefuse()
    {
        // The real caller: HumanService.CreateAsync builds the body, so drive it rather than restating it.
        JsonElement? built = null;
        var humans = new Mock<Pgan.PoracleWebNet.Core.Abstractions.Services.IPoracleHumanProxy>();
        humans.Setup(h => h.CreateHumanAsync(It.IsAny<JsonElement>()))
            .Callback<JsonElement>(b => built = b.Clone())
            .Returns(Task.CompletedTask);
        humans.Setup(h => h.GetHumanAsync(It.IsAny<string>())).ReturnsAsync((JsonElement?)null);
        var service = new HumanService(
            humans.Object, Mock.Of<Pgan.PoracleWebNet.Core.Abstractions.Services.IPoracleTrackingProxy>());

        await service.CreateAsync(new Human { Id = "https://discord.com/api/webhooks/1/x", Name = "Hook", Type = "webhook", Enabled = 1, AdminDisable = 0 });

        var handler = ScriptedHandler.Ok("""{"status":"ok"}""");
        await CreateSut(handler).CreateHumanAsync(built!.Value);

        var sent = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal($"{ApiAddress}/api/v2/humans", sent.Url);

        var body = JsonDocument.Parse(sent.Body!).RootElement;
        var schema = CreateHumanSchema();
        foreach (var property in body.EnumerateObject())
        {
            Assert.True(schema.Properties.Contains(property.Name), $"{property.Name} is not on V2CreateHumanBody");
        }

        // Types as the schema declares them: bool where the service stores 0/1, string for the ids.
        Assert.Equal("https://discord.com/api/webhooks/1/x", body.GetProperty("id").GetString());
        Assert.Equal("Hook", body.GetProperty("name").GetString());
        Assert.Equal("webhook", body.GetProperty("type").GetString());
        Assert.Equal(JsonValueKind.True, body.GetProperty("enabled").ValueKind);
        Assert.Equal(JsonValueKind.False, body.GetProperty("admin_disable").ValueKind);
        Assert.All(schema.Required, name => Assert.True(body.TryGetProperty(name, out _), $"{name} is required"));
    }

    [Fact]
    public async Task CreateHumanSendsExactlyTheDeclaredPropertiesInBothDirections()
    {
        // Every declared property reaches the wire, and a property the schema does not declare does not
        // (additionalProperties:false makes it a 422).
        var everything = """
            {"admin_disable":false,"area":["a"],"community":["c"],"enabled":true,"id":"u","language":"en",
             "latitude":1.5,"longitude":2.5,"name":"n","notes":"x","profile_name":"p","type":"discord:user",
             "bogus":1,"uid":7}
            """;
        var handler = ScriptedHandler.Ok("""{"status":"ok"}""");

        await CreateSut(handler).CreateHumanAsync(Body(everything));

        var sent = JsonDocument.Parse(Assert.Single(handler.Requests).Body!).RootElement
            .EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
        var declared = CreateHumanSchema().Properties.OrderBy(n => n, StringComparer.Ordinal).ToList();

        Assert.Equal(declared, sent);
        Assert.Equal(12, declared.Count);
    }

    [Fact]
    public async Task CreateHumanLeavesOutWhatTheCallerDidNotSupply()
    {
        // Required are id and name only; the rest take PoracleNG's defaults, so an absent key must stay absent.
        var handler = ScriptedHandler.Ok("""{"status":"ok"}""");

        await CreateSut(handler).CreateHumanAsync(Body("""{"id":"u","name":"n"}"""));

        var body = JsonDocument.Parse(Assert.Single(handler.Requests).Body!).RootElement;
        Assert.Equal(["id", "name"], body.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));
    }

    private static (HashSet<string> Properties, List<string> Required) CreateHumanSchema()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(OpenApiFixture("5.4.0")));
        var schema = doc.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("V2CreateHumanBody");

        return (
            schema.GetProperty("properties").EnumerateObject().Select(p => p.Name).ToHashSet(),
            schema.GetProperty("required").EnumerateArray().Select(e => e.GetString()!).ToList());
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
        var sut = CreateSut(handler);

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
        var sut = CreateSut(handler);

        await sut.AddProfileAsync("user1", Body($$"""{"name":"probe-two","area":"[]","latitude":0,"longitude":0,"active_hours":{{activeHours}}}"""));

        using var body = JsonDocument.Parse(Assert.Single(handler.Requests).Body!);
        Assert.False(body.RootElement.TryGetProperty("active_hours", out _));
        AssertConformsTo(body.RootElement, "V2AddProfileBody");
    }

    [Fact]
    public async Task ASubmittedScheduleTravelsAsAnArray()
    {
        // ProfileController.Update sends only the active_hours the caller submitted (a plain rename sends
        // none, so v2 leaves the stored schedule alone); when one is submitted it goes out as v2's array.
        var handler = ScriptedHandler.Ok("""{"status":"ok"}""");
        var sut = CreateSut(handler);

        await sut.UpdateProfileAsync(
            "user1", Body("""{"profile_no":2,"name":"renamed","active_hours":"[{\"day\":1,\"hours\":8,\"mins\":0}]"}"""));

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
        var sut = CreateSut(handler);

        await sut.UpdateProfileAsync(
            "user1", Body($$"""{"profile_no":2,"name":"renamed","active_hours":{{activeHours}}}"""));

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
        var sut = CreateSut(handler);

        await sut.UpdateProfileAsync("user1", Body("""{"profile_no":2,"name":"renamed","active_hours":null}"""));

        using var body = JsonDocument.Parse(Assert.Single(handler.Requests).Body!);
        Assert.False(body.RootElement.TryGetProperty("active_hours", out _));
    }

    // ---- profile reads, delete and copy on v2 (#995) -----------------------------------------------

    // The shapes a live 5.3.0 and the 5.2.1 document answer for the same two profiles. The second one
    // carries what PoracleNG writes for a schedule entered as strings, which v2 hands back as integers.
    private const string ProfilesV2Develop = """
        {"profiles":[
          {"uid":497,"id":"user1","profile_no":2,"name":"p2","area":["aberdeen"],"latitude":0,"longitude":0,
           "active_hours":[]},
          {"uid":498,"id":"user1","profile_no":3,"name":"p3","area":["aberdeen","animal kingdom"],
           "latitude":40.25,"longitude":-75.5,
           "active_hours":[{"day":1,"hours":8,"mins":0},{"day":2,"hours":9,"mins":15,"step":2,"end_hours":17,"end_mins":0}]}]}
        """;

    private const string ProfilesV2Released = """
        {"profiles":[
          {"uid":497,"id":"user1","profile_no":2,"name":"p2","area":"[\"aberdeen\"]","latitude":0,"longitude":0,
           "active_hours":[]},
          {"uid":498,"id":"user1","profile_no":3,"name":"p3","area":"[\"aberdeen\",\"animal kingdom\"]",
           "latitude":40.25,"longitude":-75.5,
           "active_hours":[{"day":1,"hours":8,"mins":0},{"day":2,"hours":9,"mins":15,"step":2,"end_hours":17,"end_mins":0}]}]}
        """;

    [Theory]
    [InlineData(ProfilesV2Develop)]
    [InlineData(ProfilesV2Released)]
    public async Task TheV2ProfileListReachesProfileServiceInV1sShape(string payload)
    {
        var handler = ScriptedHandler.Ok(payload);
        var sut = CreateSut(handler);

        var profile = await new ProfileService(sut).GetByUserAndProfileNoAsync("user1", 3);

        Assert.Equal($"{ApiAddress}/api/v2/humans/user1/profiles", Assert.Single(handler.Requests).Url);
        Assert.NotNull(profile);
        Assert.Equal("user1", profile.Id);
        Assert.Equal("p3", profile.Name);
        Assert.Equal(40.25, profile.Latitude);
        Assert.Equal(-75.5, profile.Longitude);

        // Both stay the JSON-encoded strings every consumer of Profile is written against.
        Assert.Equal(["aberdeen", "animal kingdom"], JsonSerializer.Deserialize<List<string>>(profile.Area!));
        Assert.True(ActiveHoursValidator.Validate(profile.ActiveHours).IsValid);
        using var hours = JsonDocument.Parse(profile.ActiveHours!);
        Assert.Equal(2, hours.RootElement.GetArrayLength());
        Assert.Equal(17, hours.RootElement[1].GetProperty("end_hours").GetInt32());
    }

    [Fact]
    public async Task AnUnscheduledV2ProfileReadsAsTheEmptyListV1Sends()
    {
        var handler = ScriptedHandler.Ok(ProfilesV2Develop);
        var sut = CreateSut(handler);

        var profile = await new ProfileService(sut).GetByUserAndProfileNoAsync("user1", 2);

        Assert.Equal("[]", profile!.ActiveHours);
        Assert.Equal("""["aberdeen"]""", profile.Area);
    }

    [Fact]
    public async Task AScheduleReadFromV2GoesBackThroughTheRenamePatchUnchanged()
    {
        // ProfileController.Update resends the stored schedule on every rename. What the list now hands
        // it must be something V2ProfileBody can turn back into the entries it came from.
        var read = ScriptedHandler.Ok(ProfilesV2Develop);
        var profile = await new ProfileService(CreateSut(read))
            .GetByUserAndProfileNoAsync("user1", 3);

        var write = ScriptedHandler.Ok("""{"status":"ok"}""");
        var sut = CreateSut(write);
        await sut.UpdateProfileAsync(
            "user1",
            JsonSerializer.SerializeToElement(new { profile_no = 3, name = "p3", active_hours = profile!.ActiveHours }));

        using var body = JsonDocument.Parse(Assert.Single(write.Requests).Body!);
        AssertConformsTo(body.RootElement, "V2UpdateProfileBody");
        var entries = body.RootElement.GetProperty("active_hours");
        Assert.Equal(2, entries.GetArrayLength());
        Assert.Equal(2, entries[1].GetProperty("step").GetInt32());
    }

    [Fact]
    public async Task AMissingHumanOnTheV2ListIsTheAccountGoneNotARouteMissing()
    {
        var handler = ScriptedHandler.Problem(
            HttpStatusCode.NotFound, """{"title":"Not Found","status":404,"detail":"human not found"}""");
        var sut = CreateSut(handler);

        await Assert.ThrowsAsync<AccountGoneException>(() => sut.GetProfilesAsync("nobody"));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task DeletingAProfileUsesV2WhereDeclared()
    {
        var handler = ScriptedHandler.Ok("""{"status":"ok"}""");
        var sut = CreateSut(handler);

        await sut.DeleteProfileAsync("user1", 3);

        var sent = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Delete, sent.Method);
        Assert.Equal($"{ApiAddress}/api/v2/humans/user1/profiles/3", sent.Url);
    }

    [Fact]
    public async Task CopyingAProfileNamesTheSourceInTheBody()
    {
        var handler = ScriptedHandler.Ok("""{"status":"ok"}""");
        var sut = CreateSut(handler);

        await sut.CopyProfileAsync("user1", fromProfileNo: 2, toProfileNo: 4);

        var sent = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal($"{ApiAddress}/api/v2/humans/user1/profiles/4/copy", sent.Url);
        using var body = JsonDocument.Parse(sent.Body!);
        AssertConformsTo(body.RootElement, "V2CopyProfileBody");
        Assert.Equal(2, body.RootElement.GetProperty("from_profile").GetInt32());
    }

    // ---- a new profile's areas and location through v2 (#993) ------------------------------------

    private static PoracleV2Capabilities Geography() => new()
    {
        Read = true,
    };

    [Fact]
    public async Task ANewProfilesGeographyIsWrittenToThatProfileAlone()
    {
        var handler = new ScriptedHandler(
            new Reply(HttpStatusCode.OK, """{"areas":["aberdeen","my fence"],"rejected":[]}"""),
            new Reply(HttpStatusCode.OK, """{"status":"ok"}"""));
        var sut = CreateSut(handler, capabilities: Geography());

        await sut.SetProfileGeographyAsync("user1", 4, ["Aberdeen", "my fence"], 40.25, -75.5);

        Assert.Equal(2, handler.Requests.Count);

        var areas = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, areas.Method);
        Assert.Equal($"{ApiAddress}/api/v2/humans/user1/areas?profile=4", areas.Url);
        using var areasBody = JsonDocument.Parse(areas.Body!);
        AssertConformsTo(areasBody.RootElement, "V2SetAreasBody");
        Assert.True(areasBody.RootElement.GetProperty("trusted").GetBoolean());
        Assert.Equal(
            ["aberdeen", "my fence"],
            areasBody.RootElement.GetProperty("areas").EnumerateArray().Select(e => e.GetString()).ToList());

        var location = handler.Requests[1];
        Assert.Equal($"{ApiAddress}/api/v2/humans/user1/location?profile=4", location.Url);
        using var locationBody = JsonDocument.Parse(location.Body!);
        AssertConformsTo(locationBody.RootElement, "V2LocationBody");
        Assert.Equal(40.25, locationBody.RootElement.GetProperty("latitude").GetDouble());
        Assert.Equal(-75.5, locationBody.RootElement.GetProperty("longitude").GetDouble());
    }

    [Fact]
    public async Task AnEmptyAreaListNeedsNoTrustSoItGoesThroughEvenWithAreaSecurityOn()
    {
        // Import always creates a profile with no areas. Nothing to trust, so area_security does not
        // change the request: it goes out untrusted, and the location call follows.
        var handler = new ScriptedHandler(
            new Reply(HttpStatusCode.OK, """{"areas":[],"rejected":[]}"""),
            new Reply(HttpStatusCode.OK, """{"status":"ok"}"""));
        var sut = CreateSut(handler, capabilities: Geography());

        await sut.SetProfileGeographyAsync("user1", 4, [], 0, 0);

        using var body = JsonDocument.Parse(handler.Requests[0].Body!);
        Assert.False(body.RootElement.TryGetProperty("trusted", out _));
        Assert.Equal(0, body.RootElement.GetProperty("areas").GetArrayLength());
        Assert.Equal($"{ApiAddress}/api/v2/humans/user1/location?profile=4", handler.Requests[1].Url);
    }

    [Fact]
    public async Task AMissingGeographyRouteMeansTheServerIsTooOld()
    {
        var handler = new ScriptedHandler(new Reply(HttpStatusCode.NotFound, RouteMissing, "text/plain"));
        var sut = CreateSut(handler, capabilities: Geography());

        var ex = await Assert.ThrowsAsync<PoracleUnsupportedException>(
            () => sut.SetProfileGeographyAsync("user1", 4, ["aberdeen"], 1, 2));
        Assert.Contains("5.4.0", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AGeographyWriteV2RefusesIsARefusal()
    {
        var handler = ScriptedHandler.Problem(
            HttpStatusCode.UnprocessableEntity,
            """{"title":"Unprocessable Entity","status":422,"detail":"invalid latitude"}""");
        var sut = CreateSut(handler, capabilities: Geography());

        await Assert.ThrowsAsync<PoracleRequestRefusedException>(
            () => sut.SetProfileGeographyAsync("user1", 4, [], 91, 0));
    }

    // ---- profiles are v2 only (5.4.0 minimum) --------------------------------------------------
    //
    // None of these declares a capability: the minimum release carries every route, so the schema does
    // not decide anything and there is no v1 to fall back to.

    [Fact]
    public async Task ACreateThatAnswersWithoutTheNumberIsRefused()
    {
        var handler = ScriptedHandler.Ok("""{"status":"ok"}""");
        var sut = CreateSut(handler);

        var refused = await Assert.ThrowsAsync<PoracleRequestRefusedException>(
            () => sut.AddProfileAsync("user1", Body("""{"name":"probe-two"}""")));

        Assert.Equal(502, refused.StatusCode);
        Assert.Contains("profile number", refused.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal($"{ApiAddress}/api/v2/humans/user1/profiles", Assert.Single(handler.Requests).Url);
    }

    [Fact]
    public async Task ARenameIsAlwaysAV2Patch()
    {
        var handler = ScriptedHandler.Ok("""{"status":"ok"}""");
        var sut = CreateSut(handler);

        await sut.UpdateProfileAsync("user1", Body("""{"profile_no":2,"name":"renamed","active_hours":null}"""));

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Patch, request.Method);
        Assert.Equal($"{ApiAddress}/api/v2/humans/user1/profiles/2", request.Url);
        Assert.Equal("renamed", JsonDocument.Parse(request.Body!).RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public async Task AScheduleV2CannotTakeIsRefusedNotSentToV1()
    {
        var handler = ScriptedHandler.Ok("""{"status":"ok"}""");
        var sut = CreateSut(handler);

        await Assert.ThrowsAsync<PoracleRequestRefusedException>(() => sut.UpdateProfileAsync(
            "user1", Body("""{"profile_no":2,"name":"x","active_hours":"[{\"day\":0,\"hours\":8,\"mins\":0}]"}""")));
        await Assert.ThrowsAsync<PoracleRequestRefusedException>(() => sut.AddProfileAsync(
            "user1", Body("""{"name":"x","active_hours":"not json"}""")));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task AnUpdateWithNoProfileNumberIsAnError()
    {
        var handler = ScriptedHandler.Ok("""{"status":"ok"}""");
        var sut = CreateSut(handler);

        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.UpdateProfileAsync("user1", Body("""{"name":"renamed"}""")));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task TheProfileCallsRequestOnlyTheirV2Url()
    {
        var handler = ScriptedHandler.Ok(ProfilesV2Develop);
        var sut = CreateSut(handler);

        await sut.GetProfilesAsync("user1");
        await sut.DeleteProfileAsync("user1", 3);
        await sut.CopyProfileAsync("user1", 2, 4);

        Assert.Equal(
            [
                $"{ApiAddress}/api/v2/humans/user1/profiles",
                $"{ApiAddress}/api/v2/humans/user1/profiles/3",
                $"{ApiAddress}/api/v2/humans/user1/profiles/4/copy",
            ],
            handler.Requests.Select(r => r.Url).ToList());
    }

    [Theory]
    [InlineData("list")]
    [InlineData("delete")]
    [InlineData("copy")]
    [InlineData("add")]
    [InlineData("update")]
    public async Task AMissingProfileRouteMeansTheServerIsTooOld(string call)
    {
        var handler = new ScriptedHandler(new Reply(HttpStatusCode.NotFound, RouteMissing, "text/plain"));
        var sut = CreateSut(handler);

        var ex = await Assert.ThrowsAsync<PoracleUnsupportedException>(() => call switch
        {
            "list" => sut.GetProfilesAsync("user1"),
            "delete" => sut.DeleteProfileAsync("user1", 3),
            "copy" => sut.CopyProfileAsync("user1", 2, 4),
            "add" => sut.AddProfileAsync("user1", Body("""{"name":"x"}""")),
            _ => sut.UpdateProfileAsync("user1", Body("""{"profile_no":2,"name":"x"}""")),
        });

        Assert.Contains("5.4.0", ex.Message, StringComparison.Ordinal);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ANewProfilesAreasAreAlwaysSentTrustedSoADuplicateKeepsTheUsersOwnFences()
    {
        // Used to go untrusted where area_security was on or unconfirmed, which dropped every drawn fence
        // from a duplicated profile (they are userSelectable:false). PoracleNG 5.4.0 is the minimum, so
        // trusted is always available and there is nothing to gate.
        var handler = new ScriptedHandler(
            new Reply(HttpStatusCode.OK, """{"areas":["aberdeen"],"rejected":[]}"""),
            new Reply(HttpStatusCode.OK, """{"status":"ok"}"""));
        var sut = CreateSut(handler);

        await sut.SetProfileGeographyAsync("user1", 4, ["aberdeen", "my fence"], 1, 2);

        Assert.Equal(2, handler.Requests.Count);
        using var body = JsonDocument.Parse(handler.Requests[0].Body!);
        Assert.True(body.RootElement.GetProperty("trusted").GetBoolean());
        Assert.Equal(
            ["aberdeen", "my fence"],
            body.RootElement.GetProperty("areas").EnumerateArray().Select(e => e.GetString()).ToList());
        Assert.Equal($"{ApiAddress}/api/v2/humans/user1/location?profile=4", handler.Requests[1].Url);
    }

    [Fact]
    public async Task ANewProfilesRejectedAreaIsDroppedAndTheLocationIsStillWritten()
    {
        var handler = new ScriptedHandler(
            new Reply(HttpStatusCode.OK, """{"areas":["aberdeen"],"rejected":["Not Loaded Yet"]}"""),
            new Reply(HttpStatusCode.OK, """{"status":"ok"}"""));
        var sut = CreateSut(handler);

        await sut.SetProfileGeographyAsync("user1", 4, ["aberdeen", "not loaded yet"], 1, 2);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal($"{ApiAddress}/api/v2/humans/user1/location?profile=4", handler.Requests[1].Url);
    }

    /// <summary>
    /// Every property of <paramref name="body"/> is one the named schema declares, recursing into
    /// <c>active_hours</c> entries -- the schemas set <c>additionalProperties:false</c>, so anything else
    /// is a 422. Read from the fixture captured off a running build rather than restated here.
    /// </summary>
    private static void AssertConformsTo(JsonElement body, string schema)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(OpenApiFixture("5.4.0")));
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

    // ---- v2 area operations (sub-project 2) ----

    [Fact]
    public async Task SetAreasTrustedPostsTheWholeListWithTrustedAndReturnsRejected()
    {
        var handler = ScriptedHandler.Ok("""{"areas":["aberdeen"],"rejected":["ghost"]}""");
        var sut = CreateSut(handler);

        var rejected = await sut.SetAreasTrustedAsync("user1", ["aberdeen", "ghost"]);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal($"{ApiAddress}/api/v2/humans/user1/areas", request.Url);
        using var body = JsonDocument.Parse(request.Body!);
        AssertConformsTo(body.RootElement, "V2SetAreasBody");
        Assert.True(body.RootElement.GetProperty("trusted").GetBoolean());
        Assert.Equal(["aberdeen", "ghost"], body.RootElement.GetProperty("areas").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(["ghost"], rejected);
    }

    [Theory]
    [InlineData("""{"areas":["a"],"rejected":null}""")]
    [InlineData("""{"areas":["a"],"rejected":[]}""")]
    [InlineData("""{"areas":["a"]}""")]
    public async Task SetAreasTrustedReturnsEmptyWhenNothingWasRejected(string payload)
    {
        var rejected = await CreateSut(ScriptedHandler.Ok(payload)).SetAreasTrustedAsync("user1", ["a"]);

        Assert.Empty(rejected);
    }

    [Fact]
    public async Task SetAreasTrustedEscapesAWebhookId()
    {
        var handler = ScriptedHandler.Ok("""{"areas":[],"rejected":null}""");

        await CreateSut(handler).SetAreasTrustedAsync("http://h:1/x", []);

        Assert.Equal($"{ApiAddress}/api/v2/humans/http%3A%2F%2Fh%3A1%2Fx/areas", Assert.Single(handler.Requests).Url);
    }

    [Fact]
    public async Task GetAreaListReadsTheActiveProfilesAreasLowercased()
    {
        var handler = ScriptedHandler.Ok("""{"human":{"id":"user1","area":"[\"Aberdeen\",\"Airport\"]"}}""");

        var areas = await CreateSut(handler).GetAreaListAsync("user1");

        Assert.Equal(["aberdeen", "airport"], areas);
        Assert.Equal($"{ApiAddress}/api/v2/humans/user1", Assert.Single(handler.Requests).Url);
    }

    [Fact]
    public async Task GetAreaListThrowsWhenTheHumanIsGone()
    {
        var handler = ScriptedHandler.Problem(
            HttpStatusCode.NotFound, """{"title":"Not Found","status":404,"detail":"human not found"}""");

        await Assert.ThrowsAnyAsync<Exception>(() => CreateSut(handler).GetAreaListAsync("user1"));
    }

    [Fact]
    public async Task GetAreaListThrowsWhenPoracleCannotAnswer()
    {
        var handler = ScriptedHandler.Problem(
            HttpStatusCode.ServiceUnavailable, """{"title":"Service Unavailable","status":503,"detail":"db"}""");

        await Assert.ThrowsAsync<HttpRequestException>(() => CreateSut(handler).GetAreaListAsync("user1"));
    }

    [Fact]
    public async Task GetAvailableAreasReadsNameSelectabilityAndExemption()
    {
        var handler = ScriptedHandler.Ok(
            """{"areas":[{"name":"Aberdeen","group":"g","userSelectable":true,"exemptFromAreaSecurity":false},{"name":"hq","userSelectable":false,"exemptFromAreaSecurity":true}]}""");

        var areas = await CreateSut(handler).GetAvailableAreasAsync("user1");

        Assert.Equal(
            [new V2AreaInfo("Aberdeen", true, false), new V2AreaInfo("hq", false, true)],
            areas);
        Assert.Equal($"{ApiAddress}/api/v2/humans/user1/areas", Assert.Single(handler.Requests).Url);
    }

    [Fact]
    public async Task GetAvailableAreasTreatsANullListAsEmpty()
    {
        var areas = await CreateSut(ScriptedHandler.Ok("""{"areas":null}""")).GetAvailableAreasAsync("user1");

        Assert.Empty(areas);
    }

    [Fact]
    public async Task RemoveAreaEverywhereSendsNameAndAllProfilesAndParsesTheAnswer()
    {
        var handler = ScriptedHandler.Ok(
            """{"name":"aberdeen","removed_from":[1,2],"broadened":{"raid":[672,673]},"dry_run":false}""");

        var result = await CreateSut(handler).RemoveAreaEverywhereAsync("user1", "aberdeen", dryRun: false);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.Equal($"{ApiAddress}/api/v2/humans/user1/areas?name=aberdeen&all_profiles=true", request.Url);
        Assert.Equal([1, 2], result.RemovedFrom);
        Assert.Equal([672, 673], result.Broadened["raid"]);
        Assert.False(result.DryRun);
    }

    [Fact]
    public async Task RemoveAreaEverywhereDryRunAddsTheFlagAndEncodesTheName()
    {
        var handler = ScriptedHandler.Ok("""{"name":"a b&c","removed_from":[1],"broadened":{},"dry_run":true}""");

        var result = await CreateSut(handler).RemoveAreaEverywhereAsync("user1", "a b&c", dryRun: true);

        Assert.Equal(
            // Uri.ToString() shows a space decoded; the wire form is %20, and the &-escape is what matters here.
            $"{ApiAddress}/api/v2/humans/user1/areas?name=a b%26c&all_profiles=true&dry_run=true",
            Assert.Single(handler.Requests).Url);
        Assert.Empty(result.Broadened);
        Assert.True(result.DryRun);
    }

    [Theory]
    [InlineData("""{"name":"x","removed_from":null,"broadened":null,"dry_run":false}""")]
    [InlineData("""{"name":"x","dry_run":false}""")]
    public async Task RemoveAreaEverywhereReadsNullAndMissingAsEmpty(string payload)
    {
        var result = await CreateSut(ScriptedHandler.Ok(payload)).RemoveAreaEverywhereAsync("user1", "x", dryRun: false);

        Assert.Empty(result.RemovedFrom);
        Assert.Empty(result.Broadened);
    }

    [Fact]
    public async Task RemoveAreaEverywhereReadsANullUidListInsideBroadenedAsEmpty()
    {
        var result = await CreateSut(ScriptedHandler.Ok("""{"removed_from":[1],"broadened":{"raid":null},"dry_run":false}"""))
            .RemoveAreaEverywhereAsync("user1", "x", dryRun: false);

        Assert.Empty(result.Broadened["raid"]);
    }

    [Fact]
    public async Task RenameAreaEverywhereSendsFromToAllProfilesAndTrusted()
    {
        var handler = ScriptedHandler.Ok("""{"from":"aiea","to":"airport","renamed_in":[1],"dry_run":false}""");

        var result = await CreateSut(handler).RenameAreaEverywhereAsync("user1", "aiea", "airport", dryRun: false);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Patch, request.Method);
        Assert.Equal(
            $"{ApiAddress}/api/v2/humans/user1/areas?from=aiea&to=airport&all_profiles=true&trusted=true",
            request.Url);
        Assert.Equal([1], result.RenamedIn);
        Assert.False(result.DryRun);
    }

    [Fact]
    public async Task RenameAreaEverywhereDryRunAddsTheFlagAndEncodesBothNames()
    {
        var handler = ScriptedHandler.Ok("""{"from":"a b","to":"c&d","renamed_in":null,"dry_run":true}""");

        var result = await CreateSut(handler).RenameAreaEverywhereAsync("user1", "a b", "c&d", dryRun: true);

        Assert.Equal(
            $"{ApiAddress}/api/v2/humans/user1/areas?from=a b&to=c%26d&all_profiles=true&trusted=true&dry_run=true",
            Assert.Single(handler.Requests).Url);
        Assert.Empty(result.RenamedIn);
        Assert.True(result.DryRun);
    }

    public static TheoryData<string> AreaCalls =>
        ["set", "list", "available", "remove", "rename"];

    private static Task CallAreaOperation(PoracleHumanProxy sut, string which) => which switch
    {
        "set" => sut.SetAreasTrustedAsync("user1", ["a"]),
        "list" => sut.GetAreaListAsync("user1"),
        "available" => sut.GetAvailableAreasAsync("user1"),
        "remove" => sut.RemoveAreaEverywhereAsync("user1", "a", false),
        _ => sut.RenameAreaEverywhereAsync("user1", "a", "b", false),
    };

    [Theory]
    [MemberData(nameof(AreaCalls))]
    public async Task EveryNewAreaRouteReportsAServerWithoutItAsUnsupported(string which)
    {
        var handler = new ScriptedHandler(new Reply(HttpStatusCode.NotFound, RouteMissing, "text/plain"));

        await Assert.ThrowsAsync<PoracleUnsupportedException>(() => CallAreaOperation(CreateSut(handler), which));
    }

    [Theory]
    [InlineData("set")]
    [InlineData("available")]
    [InlineData("remove")]
    [InlineData("rename")]
    public async Task EveryNewAreaRouteCarriesPoracleNgsDetailOnARefusal(string which)
    {
        var handler = ScriptedHandler.Problem(
            HttpStatusCode.UnprocessableEntity, """{"title":"Unprocessable Entity","status":422,"detail":"to is not a loaded fence"}""");

        var ex = await Assert.ThrowsAsync<PoracleRequestRefusedException>(() => CallAreaOperation(CreateSut(handler), which));

        Assert.Contains("to is not a loaded fence", ex.Message, StringComparison.Ordinal);
    }

    private static JsonElement Body(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static PoracleHumanProxy CreateSut(
        ScriptedHandler handler,
        PoracleV2Capabilities? capabilities = null)
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
            PoracleHumanProxyTests.V2Schema(capabilities),
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
