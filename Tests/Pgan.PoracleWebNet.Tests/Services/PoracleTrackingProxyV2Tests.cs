using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// The pokemon write path on PoracleNG's strict <c>/api/v2</c> surface, and every way it must decline to
/// use it. See #805.
/// </summary>
/// <remarks>
/// <para>
/// Every response shape here was taken from a live 5.2.1 instance rather than from the Go source: the
/// uid-rotating 200, the problem+json 404 and 409, the 422 with its <c>errors[]</c>, and 5.1.0's plaintext
/// <c>404 page not found</c>. CLAUDE.md is explicit that source and running binary have disagreed before.
/// </para>
/// <para>
/// The translator is exercised through the proxy on purpose. What matters is the bytes on the wire —
/// <c>V2PokemonRule</c> sets <c>additionalProperties: false</c>, so one stray <c>ping</c> is a 422 — and a
/// test of the translator in isolation would assert the translator's own model of the request instead.
/// </para>
/// </remarks>
public class PoracleTrackingProxyV2Tests
{
    private const string ApiAddress = "http://localhost:3030";

    /// <summary>A stored pokemon row exactly as v1 returns it, metadata and all.</summary>
    private const string StoredRow = """
        {
          "uid": 36486, "id": "user1", "profile_no": 1, "ping": "", "description": "**Pikachu**",
          "clean": 3, "distance": 1000, "template": "1", "pokemon_id": 25, "form": 0, "costume": 9000,
          "min_iv": 90, "max_iv": 100, "gender": 2, "pvp_ranking_league": 1500, "rarity": -1,
          "size": -1, "override_location_label": "", "override_areas": null
        }
        """;

    private const string RotatedOk = """
        {"created":null,"updated":[{"pokemon_id":25,"min_iv":90,"uid":36487}],"unchanged":null}
        """;

    [Fact]
    public async Task V2PutSendsTheTranslatedRuleAndReportsTheNewUid()
    {
        var handler = ScriptedHandler.Ok(RotatedOk);
        var sut = CreateSut(handler, version: "5.2.1");

        var result = await sut.UpdateByUidAsync("pokemon", "user1", 36486, Row(StoredRow));

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal(
            $"{ApiAddress}/api/v2/humans/user1/tracking/pokemon/36486?silent=true",
            request.Url);

        using var body = JsonDocument.Parse(request.Body!);
        var sent = body.RootElement;

        // Addressing and presentation. v2 rejects the whole write if any of them arrive.
        foreach (var name in new[] { "uid", "id", "profile_no", "ping", "description" })
        {
            Assert.False(sent.TryGetProperty(name, out _), $"{name} must not reach /api/v2");
        }

        // clean 3 is auto-delete + edit-in-place, and v2 wants three booleans rather than the mask.
        Assert.True(sent.GetProperty("clean").GetBoolean());
        Assert.True(sent.GetProperty("edit").GetBoolean());
        Assert.False(sent.GetProperty("summary").GetBoolean());

        Assert.Equal("female", sent.GetProperty("gender").GetString());
        Assert.Equal(1500, sent.GetProperty("pvp_ranking_league").GetInt32());
        Assert.Equal(90, sent.GetProperty("min_iv").GetInt32());
        Assert.Equal(JsonValueKind.Null, sent.GetProperty("override_areas").ValueKind);

        Assert.Equal(36487, result.Uid);
        Assert.True(result.UsedV2);
    }

    [Fact]
    public async Task AServerWithoutV2GetsTheSameV1WriteItAlwaysGot()
    {
        var handler = ScriptedHandler.Ok("""{"newUids":[36486],"alreadyPresent":0,"updates":1,"insert":0}""");
        var sut = CreateSut(handler, version: "5.1.0");

        var result = await sut.UpdateByUidAsync("pokemon", "user1", 36486, Row(StoredRow));

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal($"{ApiAddress}/api/tracking/pokemon/user1?silent=true", request.Url);

        // Byte-identical to the body it was handed: no translation, nothing stripped, nothing added.
        Assert.Equal(Row(StoredRow).GetRawText(), request.Body);
        Assert.Equal(36486, result.Uid);
        Assert.False(result.UsedV2);
    }

    [Fact]
    public async Task AnUnreachableServerWritesThroughV1()
    {
        var handler = ScriptedHandler.Ok("""{"newUids":[],"alreadyPresent":0,"updates":1,"insert":0}""");
        var sut = CreateSut(handler, version: null);

        var result = await sut.UpdateByUidAsync("pokemon", "user1", 36486, Row(StoredRow));

        Assert.Equal(HttpMethod.Post, Assert.Single(handler.Requests).Method);

        // No uid named: the alarm stays where it was rather than moving to 0.
        Assert.Equal(36486, result.Uid);
    }

    [Fact]
    public async Task TheOtherNineTypesStayOnV1EvenOnA521Server()
    {
        // #805 is a pilot on pokemon. Each remaining type needs its own field translation derived from its
        // own schema, and v1 is frozen and unchanged on 5.2.1, so leaving them is a no-op not a deferral.
        var handler = ScriptedHandler.Ok("""{"newUids":[9],"alreadyPresent":0,"updates":1,"insert":0}""");
        var sut = CreateSut(handler, version: "5.2.1");

        await sut.UpdateByUidAsync("raid", "user1", 9, Row("""{"uid":9,"pokemon_id":9000,"level":5}"""));

        Assert.Equal($"{ApiAddress}/api/tracking/raid/user1?silent=true", Assert.Single(handler.Requests).Url);
    }

    [Theory]
    [InlineData("v1", "5.2.1", "/api/tracking/pokemon/user1?silent=true")]
    [InlineData("v2", "5.1.0", "/api/v2/humans/user1/tracking/pokemon/36486?silent=true")]
    [InlineData("auto", "5.2.1", "/api/v2/humans/user1/tracking/pokemon/36486?silent=true")]
    public async Task TheOperatorOverridePinsTheSurface(string setting, string version, string expectedPath)
    {
        // A fork can carry v2 while reporting an older number, or report 5.2.1 without it. A version probe
        // cannot see either, so the operator gets the last word.
        var handler = ScriptedHandler.Ok(RotatedOk);
        var sut = CreateSut(handler, version: version, trackingApiVersion: setting);

        await sut.UpdateByUidAsync("pokemon", "user1", 36486, Row(StoredRow));

        Assert.Equal(ApiAddress + expectedPath, Assert.Single(handler.Requests).Url);
    }

    [Fact]
    public async Task AMissingV2RouteFallsBackToV1RatherThanFailingTheEdit()
    {
        // gin answers an absent route with plaintext, huma answers an absent rule with problem+json. A
        // server that reports 5.2.0+ without carrying the route would otherwise break every pokemon edit.
        var handler = new ScriptedHandler(
            new Reply(HttpStatusCode.NotFound, "404 page not found", "text/plain"),
            new Reply(HttpStatusCode.OK, """{"newUids":[36486],"alreadyPresent":0,"updates":1,"insert":0}"""));
        var sut = CreateSut(handler, version: "5.2.1");

        var result = await sut.UpdateByUidAsync("pokemon", "user1", 36486, Row(StoredRow));

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal($"{ApiAddress}/api/tracking/pokemon/user1?silent=true", handler.Requests[1].Url);
        Assert.Equal(36486, result.Uid);
        Assert.False(result.UsedV2);
    }

    [Fact]
    public async Task AMissingV2RouteIsRememberedSoTheNextEditDoesNotProbeAgain()
    {
        var handler = new ScriptedHandler(
            new Reply(HttpStatusCode.NotFound, "404 page not found", "text/plain"),
            new Reply(HttpStatusCode.OK, """{"newUids":[36486],"alreadyPresent":0,"updates":1,"insert":0}"""),
            new Reply(HttpStatusCode.OK, """{"newUids":[36486],"alreadyPresent":0,"updates":1,"insert":0}"""));
        var cache = new MemoryCache(new MemoryCacheOptions());
        var sut = CreateSut(handler, version: "5.2.1", cache: cache);

        await sut.UpdateByUidAsync("pokemon", "user1", 36486, Row(StoredRow));
        await sut.UpdateByUidAsync("pokemon", "user1", 36486, Row(StoredRow));

        Assert.Equal(3, handler.Requests.Count);
        Assert.All(handler.Requests.Skip(1), r => Assert.Equal(HttpMethod.Post, r.Method));
    }

    [Fact]
    public async Task ARuleThatIsNotTheirsIsReportedAsNotFound()
    {
        var handler = ScriptedHandler.Problem(
            HttpStatusCode.NotFound,
            """{"title":"Not Found","status":404,"detail":"pokemon rule 999999 not found for this human"}""");
        var sut = CreateSut(handler, version: "5.2.1");

        var ex = await Assert.ThrowsAsync<TrackingRuleNotFoundException>(
            () => sut.UpdateByUidAsync("pokemon", "user1", 999999, Row(StoredRow)));

        Assert.Equal("pokemon", ex.TrackingType);
        Assert.Contains("not found for this human", ex.Message, StringComparison.Ordinal);

        // Not the route being absent: a problem+json 404 must not poison the capability cache.
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task AnAccountThatIsGoneIsReportedAsGone()
    {
        var handler = ScriptedHandler.Problem(
            HttpStatusCode.NotFound,
            """{"title":"Not Found","status":404,"detail":"human not found"}""");
        var sut = CreateSut(handler, version: "5.2.1");

        await Assert.ThrowsAsync<AccountGoneException>(
            () => sut.UpdateByUidAsync("pokemon", "user1", 36486, Row(StoredRow)));
    }

    [Fact]
    public async Task ADuplicateReplacementCarriesPoraclesOwnWordingToTheDialog()
    {
        // The edit dialog shows err.error.error. A fixed string here would re-break #496.
        var handler = ScriptedHandler.Problem(
            HttpStatusCode.Conflict,
            """{"title":"Conflict","status":409,"detail":"an identical rule already exists (uid 36488)"}""");
        var sut = CreateSut(handler, version: "5.2.1");

        var ex = await Assert.ThrowsAsync<TrackingConflictException>(
            () => sut.UpdateByUidAsync("pokemon", "user1", 36486, Row(StoredRow)));

        Assert.Equal("an identical rule already exists (uid 36488)", ex.Message);
        Assert.Equal("pokemon", ex.TrackingType);
    }

    [Fact]
    public async Task AValidationFailureNamesTheFieldRatherThanTheJsonPointer()
    {
        // v2 validates at 422 where v1 used 400, and its location is body.x on a PUT and body[0].x on a
        // POST. "body[0].pokemon_id: expected integer" teaches the user nothing the field name does not.
        var handler = ScriptedHandler.Problem(
            HttpStatusCode.UnprocessableEntity,
            """
            {"title":"Unprocessable Entity","status":422,"detail":"validation failed",
             "errors":[{"message":"expected integer","location":"body.pokemon_id","value":"twenty-five"}]}
            """);
        var sut = CreateSut(handler, version: "5.2.1");

        var ex = await Assert.ThrowsAsync<AlarmValidationException>(
            () => sut.UpdateByUidAsync("pokemon", "user1", 36486, Row(StoredRow)));

        Assert.Equal("pokemon_id: expected integer", ex.Message);
    }

    [Fact]
    public async Task ASemanticRefusalCarriesItsDetailEvenWithNoFieldErrors()
    {
        var handler = ScriptedHandler.Problem(
            HttpStatusCode.UnprocessableEntity,
            """
            {"title":"Unprocessable Entity","status":422,
             "detail":"override_areas and distance are mutually exclusive"}
            """);
        var sut = CreateSut(handler, version: "5.2.1");

        var ex = await Assert.ThrowsAsync<AlarmValidationException>(
            () => sut.UpdateByUidAsync("pokemon", "user1", 36486, Row(StoredRow)));

        Assert.Equal("override_areas and distance are mutually exclusive", ex.Message);
    }

    // ──────────────────────────────────────────────────────────────
    // The translator declines rather than guesses. Each of these is a row v1 stores happily and v2 either
    // has no place for or would refuse, and each must still save.
    // ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("""{"uid":7,"pokemon_id":25,"some_field_from_a_newer_poracle":4}""")]
    [InlineData("""{"uid":7,"pokemon_id":25,"pvp_ranking_league":1234}""")]
    [InlineData("""{"uid":7,"pokemon_id":25,"gender":9}""")]
    [InlineData("""{"uid":7,"pokemon_id":25,"clean":9}""")]
    [InlineData("""{"uid":7,"distance":1000}""")]
    public async Task ARowV2CannotCarryFaithfullyGoesToV1InsteadOfFailing(string row)
    {
        // Refusing outright would mean a PoracleNG newer than this build broke every pokemon edit;
        // dropping the field silently would be #730 again. Falling back to the frozen surface does neither.
        var handler = ScriptedHandler.Ok("""{"newUids":[7],"alreadyPresent":0,"updates":1,"insert":0}""");
        var sut = CreateSut(handler, version: "5.2.1");

        var result = await sut.UpdateByUidAsync("pokemon", "user1", 7, Row(row));

        var request = Assert.Single(handler.Requests);
        Assert.Equal($"{ApiAddress}/api/tracking/pokemon/user1?silent=true", request.Url);
        Assert.Equal(row.Replace(" ", string.Empty, StringComparison.Ordinal), request.Body);
        Assert.Equal(7, result.Uid);
    }

    [Fact]
    public async Task EveryLeagueAndGenderThatIsActuallyStoredStillGoesToV2()
    {
        // The legitimate-case half. Queried against the 5.2.1 copy of production: pvp_ranking_league holds
        // exactly 0, 500, 1500 and 2500, and gender holds 0, 1 and 2. Nothing live must fall back.
        foreach (var league in new[] { 0, 500, 1500, 2500 })
        {
            foreach (var gender in new[] { 0, 1, 2, 3 })
            {
                var handler = ScriptedHandler.Ok(RotatedOk);
                var sut = CreateSut(handler, version: "5.2.1");

                await sut.UpdateByUidAsync(
                    "pokemon",
                    "user1",
                    36486,
                    Row($$"""{"uid":36486,"pokemon_id":25,"pvp_ranking_league":{{league}},"gender":{{gender}}}"""));

                Assert.Equal(HttpMethod.Put, Assert.Single(handler.Requests).Method);
            }
        }
    }

    [Theory]
    [InlineData(0, false, false, false)]
    [InlineData(1, true, false, false)]
    [InlineData(2, false, true, false)]
    [InlineData(4, false, false, true)]
    [InlineData(7, true, true, true)]
    public async Task EveryCleanBitmaskBecomesItsThreeBooleans(int mask, bool clean, bool edit, bool summary)
    {
        var handler = ScriptedHandler.Ok(RotatedOk);
        var sut = CreateSut(handler, version: "5.2.1");

        await sut.UpdateByUidAsync(
            "pokemon", "user1", 36486, Row($$"""{"uid":36486,"pokemon_id":25,"clean":{{mask}}}"""));

        using var body = JsonDocument.Parse(Assert.Single(handler.Requests).Body!);
        Assert.Equal(clean, body.RootElement.GetProperty("clean").GetBoolean());
        Assert.Equal(edit, body.RootElement.GetProperty("edit").GetBoolean());
        Assert.Equal(summary, body.RootElement.GetProperty("summary").GetBoolean());
    }

    [Fact]
    public async Task AUserDrawnGeofenceStillReachesV2AsAnArray()
    {
        var handler = ScriptedHandler.Ok(RotatedOk);
        var sut = CreateSut(handler, version: "5.2.1");

        await sut.UpdateByUidAsync(
            "pokemon",
            "user1",
            36486,
            Row("""{"uid":36486,"pokemon_id":25,"override_areas":["terrigal"]}"""));

        using var body = JsonDocument.Parse(Assert.Single(handler.Requests).Body!);
        Assert.Equal("terrigal", body.RootElement.GetProperty("override_areas").EnumerateArray().Single().GetString());
    }

    [Fact]
    public async Task ACreateIsNotAnUpdateAndNeverTouchesV2()
    {
        // uid 0 is an insert. v2's PUT is addressed by uid and would 404; v2's POST still diffs and merges,
        // so it buys nothing. Creates stay on v1 for the pilot.
        var handler = ScriptedHandler.Ok("""{"newUids":[1],"alreadyPresent":0,"updates":0,"insert":1}""");
        var sut = CreateSut(handler, version: "5.2.1");

        await sut.UpdateByUidAsync("pokemon", "user1", 0, Row("""{"pokemon_id":25}"""));

        Assert.Equal(HttpMethod.Post, Assert.Single(handler.Requests).Method);
    }

    private static JsonElement Row(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static PoracleTrackingProxy CreateSut(
        ScriptedHandler handler,
        string? version,
        string trackingApiVersion = "auto",
        IMemoryCache? cache = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Poracle:ApiAddress"] = ApiAddress,
                ["Poracle:ApiSecret"] = "test-secret",
                ["Poracle:TrackingApiVersion"] = trackingApiVersion,
            })
            .Build();

        var profile = new Mock<IPoracleServerProfileService>();
        profile
            .Setup(p => p.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(version is null
                ? PoracleServerProfile.Unknown(DateTimeOffset.UtcNow)
                : new PoracleServerProfile { Version = version, Reachable = true, CheckedAt = DateTimeOffset.UtcNow });

        return new PoracleTrackingProxy(
            new HttpClient(handler),
            config,
            profile.Object,
            cache ?? new MemoryCache(new MemoryCacheOptions()),
            Mock.Of<ILogger<PoracleTrackingProxy>>());
    }

    private sealed record Reply(HttpStatusCode Status, string Body, string ContentType = "application/json");

    private sealed record Sent(HttpMethod Method, string Url, string? Body);

    /// <summary>Answers a fixed sequence of replies and records everything that was sent.</summary>
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
