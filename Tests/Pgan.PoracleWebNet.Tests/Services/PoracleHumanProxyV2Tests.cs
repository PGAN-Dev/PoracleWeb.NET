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
    public async Task AnUnreachableServerStaysOnV1()
    {
        // Unknown version means unknown routes. Guessing v2 costs an extra round-trip on every call
        // while PoracleNG is down, and it is down often enough for that to matter.
        var handler = ScriptedHandler.Ok("""{"human":{"id":"user1"},"status":"ok"}""");
        var sut = CreateSut(handler, version: null);

        await sut.GetHumanAsync("user1");

        Assert.Equal($"{ApiAddress}/api/humans/one/user1", Assert.Single(handler.Requests).Url);
    }

    private static PoracleHumanProxy CreateSut(ScriptedHandler handler, string? version, IMemoryCache? cache = null)
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
