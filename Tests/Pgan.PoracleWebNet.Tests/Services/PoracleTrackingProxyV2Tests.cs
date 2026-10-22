using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// The write and delete paths on PoracleNG's strict <c>/api/v2</c> surface -- routing, gating, error
/// shapes and every way they must decline to use it. Writes are exercised on pokemon; the per-type field
/// tables have their own suite in <see cref="TrackingV2TypeTranslationTests"/>. See #805 and #860.
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
        var sut = CreateSut(handler, version: "5.4.0");

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

    /// <summary>
    /// Since jfberry/PoracleNG #303 the v2 PUT keeps the rule's uid, so <c>updated[0].uid</c> equals the one
    /// addressed. The proxy still reads the response every time (a failed reassignment on the server hands
    /// back a new uid and a 200), and a kept uid must come out as itself, still reported as a v2 write.
    /// </summary>
    [Fact]
    public async Task AV2PutThatKeepsTheUidReportsIt()
    {
        var handler = ScriptedHandler.Ok(
            """{"created":null,"updated":[{"pokemon_id":25,"min_iv":90,"uid":36486}],"unchanged":null}""");
        var sut = CreateSut(handler, version: "5.4.0");

        var result = await sut.UpdateByUidAsync("pokemon", "user1", 36486, Row(StoredRow));

        Assert.Equal(36486, result.Uid);
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
    public async Task InvasionStaysOnV1BecauseItsTrackingMovesInSubProject3()
    {
        // Invasion's edit is still on v1 in this build: tracking moves to v2 one type at a time
        // (sub-project 3), and invasion's grunt-type handling goes with its move.
        var handler = ScriptedHandler.Ok("""{"newUids":[9],"alreadyPresent":0,"updates":1,"insert":0}""");
        var sut = CreateSut(handler, version: "5.4.0");

        await sut.UpdateByUidAsync("invasion", "user1", 9, Row("""{"uid":9,"grunt_type":"blanche"}"""));

        Assert.Equal($"{ApiAddress}/api/tracking/invasion/user1?silent=true", Assert.Single(handler.Requests).Url);
    }

    [Theory]
    [InlineData("v1", "5.4.0", "/api/v2/humans/user1/tracking/pokemon/36486?silent=true")]
    [InlineData("v2", "5.1.0", "/api/tracking/pokemon/user1?silent=true")]
    [InlineData("auto", "5.4.0", "/api/v2/humans/user1/tracking/pokemon/36486?silent=true")]
    public async Task ALeftoverTrackingApiVersionSettingIsIgnored(string setting, string version, string expectedPath)
    {
        // Poracle:TrackingApiVersion no longer exists: an install that still has it set gets the same
        // routing as one that never did. The server's reported version decides, 5.4.0 being the minimum.
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
        var sut = CreateSut(handler, version: "5.4.0");

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
        var sut = CreateSut(handler, version: "5.4.0", cache: cache);

        await sut.UpdateByUidAsync("pokemon", "user1", 36486, Row(StoredRow));
        await sut.UpdateByUidAsync("pokemon", "user1", 36486, Row(StoredRow));

        Assert.Equal(3, handler.Requests.Count);
        Assert.All(handler.Requests.Skip(1), r => Assert.Equal(HttpMethod.Post, r.Method));
    }

    [Fact]
    public async Task AnAbsentRouteOnOneTypeDoesNotDropTheOtherEightBackToV1()
    {
        // The absent flag is keyed per type. A single flag let one gin 404 from one route disable v2 for
        // every type at once -- and the types do not ship together, so a build that carries the raid route
        // and not the fort one is an ordinary state, not a broken server.
        var handler = new ScriptedHandler(
            new Reply(HttpStatusCode.NotFound, "404 page not found", "text/plain"),
            new Reply(HttpStatusCode.OK, """{"newUids":[63],"alreadyPresent":0,"updates":1,"insert":0}"""),
            new Reply(HttpStatusCode.OK, RotatedOk));
        var cache = new MemoryCache(new MemoryCacheOptions());
        var sut = CreateSut(handler, version: "5.4.0", cache: cache);

        await sut.UpdateByUidAsync("fort", "user1", 63, Row("""{"uid":63,"fort_type":"gym","include_empty":0}"""));
        var pokemon = await sut.UpdateByUidAsync("pokemon", "user1", 36486, Row(StoredRow));

        Assert.True(pokemon.UsedV2);
        Assert.Equal(HttpMethod.Put, handler.Requests[2].Method);
        Assert.Equal($"{ApiAddress}/api/v2/humans/user1/tracking/pokemon/36486?silent=true", handler.Requests[2].Url);
    }

    [Fact]
    public async Task ARuleThatIsNotTheirsIsReportedAsNotFound()
    {
        var handler = ScriptedHandler.Problem(
            HttpStatusCode.NotFound,
            """{"title":"Not Found","status":404,"detail":"pokemon rule 999999 not found for this human"}""");
        var sut = CreateSut(handler, version: "5.4.0");

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
        var sut = CreateSut(handler, version: "5.4.0");

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
        var sut = CreateSut(handler, version: "5.4.0");

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
        var sut = CreateSut(handler, version: "5.4.0");

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
        var sut = CreateSut(handler, version: "5.4.0");

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
        // ping is the one exception to "falling back is safe" -- see ARowWithAPingNeverFallsBackToV1Silently
        // below, and PoracleTrackingProxy.TryReplaceV2Async.
        var handler = ScriptedHandler.Ok("""{"newUids":[7],"alreadyPresent":0,"updates":1,"insert":0}""");
        var sut = CreateSut(handler, version: "5.4.0");

        var result = await sut.UpdateByUidAsync("pokemon", "user1", 7, Row(row));

        var request = Assert.Single(handler.Requests);
        Assert.Equal($"{ApiAddress}/api/tracking/pokemon/user1?silent=true", request.Url);
        Assert.Equal(row.Replace(" ", string.Empty, StringComparison.Ordinal), request.Body);
        Assert.Equal(7, result.Uid);
    }

    /// <summary>
    /// PoracleNG 5.4.0 stores <c>ping</c> on every v2 write, so a set mention is no reason to refuse an
    /// edit or to leave v2. A row v2 cannot carry for some other reason still goes to v1 unchanged.
    /// </summary>
    [Fact]
    public async Task ARowWithAPingRidesV2WithoutAnyCapabilityProbe()
    {
        const string row = """{"uid":7,"pokemon_id":25,"min_iv":90,"ping":"<@&400027130022592512>"}""";
        var handler = ScriptedHandler.Ok("""{"created":null,"updated":[{"pokemon_id":25,"min_iv":90,"uid":8}],"unchanged":null}""");
        var sut = CreateSut(handler, version: "5.4.0");

        var result = await sut.UpdateByUidAsync("pokemon", "user1", 7, Row(row));

        Assert.True(result.UsedV2);
        var request = Assert.Single(handler.Requests);
        Assert.Equal($"{ApiAddress}/api/v2/humans/user1/tracking/pokemon/7?silent=true", request.Url);
        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal("<@&400027130022592512>", body.RootElement.GetProperty("ping").GetString());
    }

    private const string MentionPing = "<@&400027130022592512>";

    [Fact]
    public async Task APingWithAnUntranslatableFieldIsRefusedAndNothingIsSent()
    {
        var handler = ScriptedHandler.Ok("""{"newUids":[7],"alreadyPresent":0,"updates":1,"insert":0}""");
        var sut = CreateSut(handler, version: "5.4.0");

        var ex = await Assert.ThrowsAsync<AlarmValidationException>(() => sut.UpdateByUidAsync(
            "pokemon", "user1", 7,
            Row($$"""{"uid":7,"pokemon_id":25,"some_field_from_a_newer_poracle":4,"ping":"{{MentionPing}}"}""")));

        Assert.Contains("mention", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task AnEmptyPingWithAnUntranslatableFieldStillEditsOnV1()
    {
        var handler = ScriptedHandler.Ok("""{"newUids":[7],"alreadyPresent":0,"updates":1,"insert":0}""");
        var sut = CreateSut(handler, version: "5.4.0");

        var result = await sut.UpdateByUidAsync(
            "pokemon", "user1", 7, Row("""{"uid":7,"pokemon_id":25,"some_field_from_a_newer_poracle":4,"ping":""}"""));

        Assert.False(result.UsedV2);
        Assert.Equal($"{ApiAddress}/api/tracking/pokemon/user1?silent=true", Assert.Single(handler.Requests).Url);
    }

    [Fact]
    public async Task APingIsKeptOnV2WhateverALeftoverTrackingApiVersionSettingSays()
    {
        var handler = ScriptedHandler.Ok("""{"newUids":[7],"alreadyPresent":0,"updates":1,"insert":0}""");
        var sut = CreateSut(handler, version: "5.4.0", trackingApiVersion: "v1");

        var result = await sut.UpdateByUidAsync(
            "pokemon", "user1", 7, Row($$"""{"uid":7,"pokemon_id":25,"ping":"{{MentionPing}}"}"""));

        Assert.True(result.UsedV2);
        Assert.Contains("/api/v2/", Assert.Single(handler.Requests).Url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APingIsRefusedWhenTheGruntTypeIsNotInTheServersMasterdata()
    {
        var handler = ScriptedHandler.Ok("""{"newUids":[412],"alreadyPresent":0,"updates":1,"insert":0}""");
        var sut = CreateSut(handler, version: "5.4.0", capabilities: WithGruntType(), gruntNames: ["fire"]);
        var row = InvasionRow.Replace("\"ping\":\"\"", $"\"ping\":\"{MentionPing}\"", StringComparison.Ordinal);

        await Assert.ThrowsAsync<AlarmValidationException>(() => sut.UpdateByUidAsync("invasion", "user1", 412, Row(row)));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task APingIsRefusedWhenTheV2RouteAnswersPlaintext404()
    {
        var handler = new ScriptedHandler(new Reply(HttpStatusCode.NotFound, "404 page not found", "text/plain"));
        var sut = CreateSut(handler, version: "5.4.0");

        await Assert.ThrowsAsync<AlarmValidationException>(() => sut.UpdateByUidAsync(
            "pokemon", "user1", 7, Row($$"""{"uid":7,"pokemon_id":25,"ping":"{{MentionPing}}"}""")));

        Assert.All(handler.Requests, r => Assert.Contains("/api/v2/", r.Url, StringComparison.Ordinal));
    }

    [Fact]
    public async Task AFilterThisServerBoundsGoesToV1WhileTheSameRowGoesToV2OnAServerThatDoesNot()
    {
        // pvp_ranking_best 0 is what PoracleWeb.NET writes for a rule with no PVP floor, and what
        // PoracleNG stores for one. An unreleased PoracleNG declares the field minimum 1, which would
        // refuse roughly sixteen thousand rules on one production instance. The limits are read from the
        // instance being written to, so the same row takes different paths on different servers, and the
        // server everyone runs today is unaffected.
        const string Row7 = @"{""uid"":7,""pokemon_id"":25,""pvp_ranking_best"":0}";

        var bounded = new ScriptedHandler(new Reply(HttpStatusCode.OK, @"{""newUids"":[7],""alreadyPresent"":0,""updates"":1,""insert"":0}"))
        {
            OpenApi = @"{""components"":{""schemas"":{""V2PokemonRule"":{""properties"":{""pvp_ranking_best"":{""minimum"":1,""maximum"":4096}}}}}}",
        };

        await CreateSut(bounded, version: "5.4.0").UpdateByUidAsync("pokemon", "user1", 7, Row(Row7));

        var toV1 = Assert.Single(bounded.Requests);
        Assert.Equal($"{ApiAddress}/api/tracking/pokemon/user1?silent=true", toV1.Url);
        Assert.Contains(@"""pvp_ranking_best"":0", toV1.Body, StringComparison.Ordinal);

        // Same row, same version, a server declaring no such limit: unchanged from today.
        var unbounded = ScriptedHandler.Ok(@"{""status"":""ok"",""uid"":7}");

        await CreateSut(unbounded, version: "5.4.0").UpdateByUidAsync("pokemon", "user1", 7, Row(Row7));

        var toV2 = Assert.Single(unbounded.Requests);
        Assert.Equal($"{ApiAddress}/api/v2/humans/user1/tracking/pokemon/7?silent=true", toV2.Url);
        Assert.Contains(@"""pvp_ranking_best"":0", toV2.Body, StringComparison.Ordinal);
    }


    [Fact]
    public async Task AnEmptyPingIsStillDroppedAndTheRowStaysOnV2()
    {
        // The legitimate-case half: 18488 of 18490 production rules carry no ping, and v2 would store the
        // same empty string whether or not this server carries #249, so those must not all be pushed onto
        // v1 by the guard above regardless of the capability.
        var handler = ScriptedHandler.Ok(RotatedOk);
        var sut = CreateSut(handler, version: "5.4.0");

        await sut.UpdateByUidAsync(
            "pokemon", "user1", 7, Row("""{"uid":7,"pokemon_id":25,"min_iv":90,"ping":""}"""));

        var request = Assert.Single(handler.Requests);
        Assert.Equal($"{ApiAddress}/api/v2/humans/user1/tracking/pokemon/7?silent=true", request.Url);

        using var body = JsonDocument.Parse(request.Body!);
        Assert.False(body.RootElement.TryGetProperty("ping", out _));
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
                var sut = CreateSut(handler, version: "5.4.0");

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
        var sut = CreateSut(handler, version: "5.4.0");

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
        var sut = CreateSut(handler, version: "5.4.0");

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
        var sut = CreateSut(handler, version: "5.4.0");

        await sut.UpdateByUidAsync("pokemon", "user1", 0, Row("""{"pokemon_id":25}"""));

        Assert.Equal(HttpMethod.Post, Assert.Single(handler.Requests).Method);
    }

    // ---- deletes (sub-project 3a) ----------------------------------------------------------------
    //
    // Deletes go through v2 and nowhere else. A delete names only the uid, so none of the translation
    // above applies and nothing is version-gated: a server without the route answers gin's plaintext 404
    // and is told it is too old, the way the reads are.
    //
    // v2 scopes a uid to the active profile, where v1 deleted it whatever profile it sat on. The UI never
    // asks for that (every controller's Delete looks the uid up on the active profile first, #889), but
    // quick-pick remove does: its tracked uids can sit on a profile the scheduler has since switched away
    // from. So a uid the active profile does not hold is looked up once across every profile and deleted
    // there with ?profile=N. Only a uid no profile holds is "already gone". A delete that succeeds on the
    // active profile makes no extra read.

    /// <summary>An all-profiles snapshot holding <paramref name="tracking"/>, with profile 1 active.</summary>
    private static string AllProfiles(string tracking = "{}") =>
        $$"""{"human":{"current_profile_no":1},"profiles":[{"profile_no":1},{"profile_no":2},{"profile_no":3}],"tracking":{{tracking}}}""";

    private static bool IsAllProfilesRead(Sent request) =>
        request.Method == HttpMethod.Get && request.Url.Contains("all_profiles=true", StringComparison.Ordinal);

    private const string RuleNotOnThisProfile = """
        {"title":"Not Found","status":404,"detail":"pokemon rule 36486 not found for this human"}
        """;

    private const string HumanNotFound = """
        {"title":"Not Found","status":404,"detail":"human not found"}
        """;

    [Fact]
    public async Task ADeleteSendsOnlyItsV2RequestAndReadsNothing()
    {
        var handler = ScriptedHandler.Ok("""{"deleted":[{"uid":36486,"pokemon_id":25}]}""");
        var sut = CreateSut(handler, version: "5.4.0");

        await sut.DeleteByUidAsync("pokemon", "user1", 36486);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.Equal($"{ApiAddress}/api/v2/humans/user1/tracking/pokemon/36486?silent=true", request.Url);
    }

    [Theory]
    [InlineData("5.1.0")]
    [InlineData(null)]
    public async Task ADeleteIsNotGatedOnTheReportedVersion(string? version)
    {
        // The version used to decide between the surfaces. There is only one surface now, and a server
        // that lacks it says so with a plaintext 404 (below), not with a version number.
        var handler = ScriptedHandler.Ok("""{"deleted":[{"uid":36486}]}""");
        var sut = CreateSut(handler, version: version);

        await sut.DeleteByUidAsync("pokemon", "user1", 36486);

        Assert.Equal(
            $"{ApiAddress}/api/v2/humans/user1/tracking/pokemon/36486?silent=true",
            Assert.Single(handler.Requests).Url);
    }

    [Fact]
    public async Task ADeleteOfARuleNoProfileHoldsIsAlreadyGoneAndNeverReachesV1()
    {
        var handler = new ScriptedHandler(
            new Reply(HttpStatusCode.NotFound, RuleNotOnThisProfile, "application/problem+json"),
            new Reply(HttpStatusCode.OK, AllProfiles()));
        var logger = new CapturingLogger();
        var sut = CreateSut(handler, version: "5.4.0", logger: logger);

        await sut.DeleteByUidAsync("pokemon", "user1", 36486);

        Assert.Equal(2, handler.Requests.Count);
        Assert.True(IsAllProfilesRead(handler.Requests[1]));
        Assert.DoesNotContain(handler.Requests, r => r.Url.Contains("/api/tracking/", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, e => e.Message.Contains("36486", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ADeleteAgainstAGoneAccountIsReportedAsGone()
    {
        // Swallowing this as "already gone" would leave a dead session looking alive (#595).
        var handler = ScriptedHandler.Problem(HttpStatusCode.NotFound, HumanNotFound);
        var sut = CreateSut(handler, version: "5.4.0");

        await Assert.ThrowsAsync<AccountGoneException>(() => sut.DeleteByUidAsync("pokemon", "user1", 36486));

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ADeleteOnAServerWithoutTheRouteIsUnsupported()
    {
        var handler = new ScriptedHandler(new Reply(HttpStatusCode.NotFound, "404 page not found", "text/plain"));
        var sut = CreateSut(handler, version: "5.4.0");

        await Assert.ThrowsAsync<PoracleUnsupportedException>(() => sut.DeleteByUidAsync("pokemon", "user1", 36486));

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ADeleteTheServerFailsIsAFailure()
    {
        var handler = ScriptedHandler.Problem(
            HttpStatusCode.InternalServerError, """{"title":"Internal Server Error","status":500,"detail":"database error"}""");
        var sut = CreateSut(handler, version: "5.4.0");

        await Assert.ThrowsAsync<HttpRequestException>(() => sut.DeleteByUidAsync("pokemon", "user1", 36486));
    }

    // ---- invasion deletes reach the Pokestop events the invasion read folds in --------------------
    //
    // The invasion read carries the incident rules (kecleon, gold-stop, showcase) as v1 did, so a uid
    // handed to an invasion delete may be an event. v2's invasion routes filter event rows out
    // (isEventGruntType in v2_incident.go): the single form 404s and the bulk form skips them. The proxy
    // therefore asks incident for whatever invasion did not take, so no caller has to know.

    private const string InvasionRuleNotFound = """
        {"title":"Not Found","status":404,"detail":"invasion rule 412 not found for this human"}
        """;

    [Fact]
    public async Task AnInvasionDeleteOfAGruntUsesOnlyTheInvasionRoute()
    {
        var handler = ScriptedHandler.Ok("""{"deleted":[{"uid":412,"grunt_type":"water"}]}""");
        var sut = CreateSut(handler, version: "5.4.0");

        await sut.DeleteByUidAsync("invasion", "user1", 412);

        Assert.Equal(
            $"{ApiAddress}/api/v2/humans/user1/tracking/invasion/412?silent=true",
            Assert.Single(handler.Requests).Url);
    }

    [Fact]
    public async Task AnInvasionDeleteOfAPokestopEventIsDeletedAsAnIncident()
    {
        var handler = new ScriptedHandler(
            new Reply(HttpStatusCode.NotFound, InvasionRuleNotFound, "application/problem+json"),
            new Reply(HttpStatusCode.OK, """{"deleted":[{"uid":412,"display_type":8}]}"""));
        var sut = CreateSut(handler, version: "5.4.0");

        await sut.DeleteByUidAsync("invasion", "user1", 412);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(HttpMethod.Delete, handler.Requests[1].Method);
        Assert.Equal($"{ApiAddress}/api/v2/humans/user1/tracking/incident/412?silent=true", handler.Requests[1].Url);
    }

    [Fact]
    public async Task AnInvasionDeleteOfAUidNeitherRouteHoldsIsAlreadyGone()
    {
        var handler = new ScriptedHandler(
            new Reply(HttpStatusCode.NotFound, InvasionRuleNotFound, "application/problem+json"),
            new Reply(
                HttpStatusCode.NotFound,
                """{"title":"Not Found","status":404,"detail":"incident rule 412 not found for this human"}""",
                "application/problem+json"),
            new Reply(HttpStatusCode.OK, AllProfiles()));
        var sut = CreateSut(handler, version: "5.4.0");

        await sut.DeleteByUidAsync("invasion", "user1", 412);

        Assert.Equal(3, handler.Requests.Count);
        Assert.True(IsAllProfilesRead(handler.Requests[2]));
    }

    [Fact]
    public async Task AnInvasionDeleteAgainstAGoneAccountIsNotRetriedAsAnIncident()
    {
        var handler = ScriptedHandler.Problem(HttpStatusCode.NotFound, HumanNotFound);
        var sut = CreateSut(handler, version: "5.4.0");

        await Assert.ThrowsAsync<AccountGoneException>(() => sut.DeleteByUidAsync("invasion", "user1", 412));

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ARuleNotFoundOnAnotherTypeIsNotRetriedAsAnIncident()
    {
        // The retry is invasion's alone: only invasion's read folds the events in.
        var handler = new ScriptedHandler(
            new Reply(HttpStatusCode.NotFound, RuleNotOnThisProfile, "application/problem+json"),
            new Reply(HttpStatusCode.OK, AllProfiles()));
        var sut = CreateSut(handler, version: "5.4.0");

        await sut.DeleteByUidAsync("pokemon", "user1", 36486);

        Assert.DoesNotContain(handler.Requests, r => r.Url.Contains("/incident", StringComparison.Ordinal));
    }

    // ---- bulk deletes ----------------------------------------------------------------------------

    [Fact]
    public async Task ABulkDeleteSendsTheUidsInTheBodyAndAsksForSilence()
    {
        // silent matters as much here as on the single form: v2PushRemoved writes one "Removed:" line per
        // row into a single DM (#848).
        var handler = ScriptedHandler.Ok("""{"deleted":[{"uid":1},{"uid":2},{"uid":3}]}""");
        var sut = CreateSut(handler, version: "5.4.0");

        await sut.BulkDeleteByUidsAsync("pokemon", "user1", [1, 2, 3]);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.Equal($"{ApiAddress}/api/v2/humans/user1/tracking/pokemon?silent=true", request.Url);
        Assert.Equal("""{"uids":[1,2,3]}""", request.Body);
    }

    [Fact]
    public async Task ABulkDeleteFarTooLongForAQueryStringIsStillOneV2Request()
    {
        // "Delete all alarms" on a heavy account. The uids travel in the body, so there is no length at
        // which they have to go anywhere else: 500 six-digit uids render to 3,499 characters.
        var uids = Enumerable.Range(100000, 500).ToList();
        Assert.True(string.Join(',', uids).Length > 3000);
        var deleted = string.Join(',', uids.Select(u => $"{{\"uid\":{u}}}"));
        var handler = ScriptedHandler.Ok($"{{\"deleted\":[{deleted}]}}");
        var sut = CreateSut(handler, version: "5.4.0");

        await sut.BulkDeleteByUidsAsync("pokemon", "user1", uids);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.Equal($"{ApiAddress}/api/v2/humans/user1/tracking/pokemon?silent=true", request.Url);
        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal(uids, body.RootElement.GetProperty("uids").EnumerateArray().Select(e => e.GetInt32()));
    }

    [Fact]
    public async Task ABulkDeleteThatV2OnlyPartlyTookLogsWhatItLeftAndDoesNotCallV1()
    {
        // v2 skips a uid it does not hold (gone already, or on another profile) and still answers 200.
        // That is an answer, not a fault, so it is logged rather than retried anywhere.
        var handler = new ScriptedHandler(
            new Reply(HttpStatusCode.OK, """{"deleted":[{"uid":1}]}"""),
            new Reply(HttpStatusCode.OK, AllProfiles()));
        var logger = new CapturingLogger();
        var sut = CreateSut(handler, version: "5.4.0", logger: logger);

        await sut.BulkDeleteByUidsAsync("pokemon", "user1", [1, 2, 3]);

        Assert.Equal(2, handler.Requests.Count);
        Assert.True(IsAllProfilesRead(handler.Requests[1]));
        var entry = Assert.Single(logger.Entries, e => e.Level == LogLevel.Information);
        Assert.Contains("2, 3", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ABulkDeleteThatTookEverythingLogsNothingAndReadsNothing()
    {
        var handler = ScriptedHandler.Ok("""{"deleted":[{"uid":1},{"uid":2}]}""");
        var logger = new CapturingLogger();
        var sut = CreateSut(handler, version: "5.4.0", logger: logger);

        await sut.BulkDeleteByUidsAsync("pokemon", "user1", [1, 2]);

        Assert.Single(handler.Requests);
        Assert.DoesNotContain(logger.Entries, e => e.Level >= LogLevel.Information);
    }

    [Fact]
    public async Task ABulkDeleteWhoseAnswerCannotBeReadDoesNotFallBackToV1()
    {
        var handler = new ScriptedHandler(
            new Reply(HttpStatusCode.OK, "not json at all"),
            new Reply(HttpStatusCode.OK, AllProfiles()));
        var sut = CreateSut(handler, version: "5.4.0");

        await sut.BulkDeleteByUidsAsync("pokemon", "user1", [1, 2]);

        Assert.DoesNotContain(handler.Requests, r => r.Url.Contains("/api/tracking/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ABulkDeleteOfNothingAsksTheServerNothing()
    {
        var handler = ScriptedHandler.Ok("""{"deleted":[]}""");
        var sut = CreateSut(handler, version: "5.4.0");

        await sut.BulkDeleteByUidsAsync("pokemon", "user1", []);

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ABulkDeleteAgainstAGoneAccountIsReportedAsGone()
    {
        var handler = ScriptedHandler.Problem(HttpStatusCode.NotFound, HumanNotFound);
        var sut = CreateSut(handler, version: "5.4.0");

        await Assert.ThrowsAsync<AccountGoneException>(() => sut.BulkDeleteByUidsAsync("pokemon", "user1", [1]));

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ABulkDeleteOnAServerWithoutTheRouteIsUnsupported()
    {
        var handler = new ScriptedHandler(new Reply(HttpStatusCode.NotFound, "404 page not found", "text/plain"));
        var sut = CreateSut(handler, version: "5.4.0");

        await Assert.ThrowsAsync<PoracleUnsupportedException>(() => sut.BulkDeleteByUidsAsync("pokemon", "user1", [1]));

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ABulkDeleteTheServerFailsIsAFailure()
    {
        var handler = ScriptedHandler.Problem(
            HttpStatusCode.InternalServerError, """{"title":"Internal Server Error","status":500,"detail":"database error"}""");
        var sut = CreateSut(handler, version: "5.4.0");

        await Assert.ThrowsAsync<HttpRequestException>(() => sut.BulkDeleteByUidsAsync("pokemon", "user1", [1]));
    }

    [Fact]
    public async Task AnInvasionBulkDeleteOfGruntsOnlyNeverAsksIncident()
    {
        var handler = ScriptedHandler.Ok("""{"deleted":[{"uid":10},{"uid":11}]}""");
        var sut = CreateSut(handler, version: "5.4.0");

        await sut.BulkDeleteByUidsAsync("invasion", "user1", [10, 11]);

        var request = Assert.Single(handler.Requests);
        Assert.Equal($"{ApiAddress}/api/v2/humans/user1/tracking/invasion?silent=true", request.Url);
    }

    [Fact]
    public async Task AnInvasionBulkDeleteOfMixedRulesSendsTheEventsToIncident()
    {
        var handler = new ScriptedHandler(
            new Reply(HttpStatusCode.OK, """{"deleted":[{"uid":10},{"uid":12}]}"""),
            new Reply(HttpStatusCode.OK, """{"deleted":[{"uid":11},{"uid":13}]}"""));
        var logger = new CapturingLogger();
        var sut = CreateSut(handler, version: "5.4.0", logger: logger);

        await sut.BulkDeleteByUidsAsync("invasion", "user1", [10, 11, 12, 13]);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal($"{ApiAddress}/api/v2/humans/user1/tracking/invasion?silent=true", handler.Requests[0].Url);
        Assert.Equal("""{"uids":[10,11,12,13]}""", handler.Requests[0].Body);
        Assert.Equal(HttpMethod.Delete, handler.Requests[1].Method);
        Assert.Equal($"{ApiAddress}/api/v2/humans/user1/tracking/incident?silent=true", handler.Requests[1].Url);
        Assert.Equal("""{"uids":[11,13]}""", handler.Requests[1].Body);
        Assert.DoesNotContain(logger.Entries, e => e.Level >= LogLevel.Information);
    }

    [Fact]
    public async Task AnInvasionBulkDeleteOfEventsOnlySendsThemAllToIncident()
    {
        var handler = new ScriptedHandler(
            new Reply(HttpStatusCode.OK, """{"deleted":null}"""),
            new Reply(HttpStatusCode.OK, """{"deleted":[{"uid":11}]}"""));
        var sut = CreateSut(handler, version: "5.4.0");

        await sut.BulkDeleteByUidsAsync("invasion", "user1", [11]);

        Assert.Equal("""{"uids":[11]}""", handler.Requests[1].Body);
    }

    [Fact]
    public async Task AnInvasionBulkDeleteLogsWhatNeitherRouteTook()
    {
        var handler = new ScriptedHandler(
            new Reply(HttpStatusCode.OK, """{"deleted":[{"uid":10}]}"""),
            new Reply(HttpStatusCode.OK, """{"deleted":[]}"""),
            new Reply(HttpStatusCode.OK, AllProfiles()));
        var logger = new CapturingLogger();
        var sut = CreateSut(handler, version: "5.4.0", logger: logger);

        await sut.BulkDeleteByUidsAsync("invasion", "user1", [10, 99]);

        var entry = Assert.Single(logger.Entries, e => e.Level == LogLevel.Information);
        Assert.Contains("99", entry.Message, StringComparison.Ordinal);
    }

    // ---- the human's own uids on a profile that is not active (fix round 1) ---------------------------

    [Fact]
    public async Task ASingleDeleteOfAUidOnAnotherProfileIsDeletedThere()
    {
        var handler = new ScriptedHandler(
            new Reply(HttpStatusCode.NotFound, RuleNotOnThisProfile, "application/problem+json"),
            new Reply(HttpStatusCode.OK, AllProfiles("""{"pokemon":[{"uid":36486,"profile_no":2,"pokemon_id":25}]}""")),
            new Reply(HttpStatusCode.OK, """{"deleted":[{"uid":36486}]}"""));
        var logger = new CapturingLogger();
        var sut = CreateSut(handler, version: "5.4.0", logger: logger);

        await sut.DeleteByUidAsync("pokemon", "user1", 36486);

        Assert.Equal(3, handler.Requests.Count);
        Assert.True(IsAllProfilesRead(handler.Requests[1]));
        Assert.Equal(HttpMethod.Delete, handler.Requests[2].Method);
        Assert.Equal(
            $"{ApiAddress}/api/v2/humans/user1/tracking/pokemon/36486?silent=true&profile=2",
            handler.Requests[2].Url);
        Assert.DoesNotContain(logger.Entries, e => e.Level >= LogLevel.Information);
    }

    [Fact]
    public async Task AnEventUidOnAnotherProfileIsDeletedAsAnIncidentThere()
    {
        // The all-profiles read folds incident into invasion; the row's grunt_type says it is an event.
        var handler = new ScriptedHandler(
            new Reply(HttpStatusCode.NotFound, InvasionRuleNotFound, "application/problem+json"),
            new Reply(
                HttpStatusCode.NotFound,
                """{"title":"Not Found","status":404,"detail":"incident rule 412 not found for this human"}""",
                "application/problem+json"),
            new Reply(HttpStatusCode.OK, AllProfiles("""{"incident":[{"uid":412,"profile_no":3,"display_type":8}]}""")),
            new Reply(HttpStatusCode.OK, """{"deleted":[{"uid":412}]}"""));
        var sut = CreateSut(handler, version: "5.4.0");

        await sut.DeleteByUidAsync("invasion", "user1", 412);

        Assert.Equal(
            $"{ApiAddress}/api/v2/humans/user1/tracking/incident/412?silent=true&profile=3",
            handler.Requests[^1].Url);
    }

    [Fact]
    public async Task ABulkDeleteOfUidsOnTwoOtherProfilesDeletesOnEach()
    {
        var handler = new ScriptedHandler
        {
            RouteReply = request =>
            {
                if (IsAllProfilesRead(request))
                {
                    return new Reply(HttpStatusCode.OK, AllProfiles("""
                        {"raid":[{"uid":1,"profile_no":1,"level":5},{"uid":2,"profile_no":2,"level":5},
                                 {"uid":3,"profile_no":3,"level":5},{"uid":4,"profile_no":2,"level":5}]}
                        """));
                }

                if (request.Url.EndsWith("profile=2", StringComparison.Ordinal))
                {
                    return new Reply(HttpStatusCode.OK, """{"deleted":[{"uid":2},{"uid":4}]}""");
                }

                if (request.Url.EndsWith("profile=3", StringComparison.Ordinal))
                {
                    return new Reply(HttpStatusCode.OK, """{"deleted":[{"uid":3}]}""");
                }

                return new Reply(HttpStatusCode.OK, """{"deleted":[{"uid":1}]}""");
            },
        };
        var logger = new CapturingLogger();
        var sut = CreateSut(handler, version: "5.4.0", logger: logger);

        await sut.BulkDeleteByUidsAsync("raid", "user1", [1, 2, 3, 4, 99]);

        var deletes = handler.Requests.Where(r => r.Method == HttpMethod.Delete).ToList();
        Assert.Equal(3, deletes.Count);
        Assert.Equal($"{ApiAddress}/api/v2/humans/user1/tracking/raid?silent=true", deletes[0].Url);
        var onTwo = Assert.Single(deletes, d => d.Url == $"{ApiAddress}/api/v2/humans/user1/tracking/raid?silent=true&profile=2");
        Assert.Equal("""{"uids":[2,4]}""", onTwo.Body);
        var onThree = Assert.Single(deletes, d => d.Url == $"{ApiAddress}/api/v2/humans/user1/tracking/raid?silent=true&profile=3");
        Assert.Equal("""{"uids":[3]}""", onThree.Body);
        Assert.Single(handler.Requests, IsAllProfilesRead);

        // Only the uid no profile holds is left to report.
        var entry = Assert.Single(logger.Entries, e => e.Level == LogLevel.Information);
        Assert.Contains("99", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("2,", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnInvasionBulkDeleteRoutesEventsOnAnotherProfileToIncidentThere()
    {
        var handler = new ScriptedHandler
        {
            RouteReply = request =>
            {
                if (IsAllProfilesRead(request))
                {
                    return new Reply(HttpStatusCode.OK, AllProfiles("""
                        {"invasion":[{"uid":10,"profile_no":2,"grunt_type":"water"}],
                         "incident":[{"uid":11,"profile_no":2,"display_type":8}]}
                        """));
                }

                return request.Url.EndsWith("profile=2", StringComparison.Ordinal)
                    ? new Reply(HttpStatusCode.OK, request.Url.Contains("/incident", StringComparison.Ordinal)
                        ? """{"deleted":[{"uid":11}]}"""
                        : """{"deleted":[{"uid":10}]}""")
                    : new Reply(HttpStatusCode.OK, """{"deleted":[]}""");
            },
        };
        var sut = CreateSut(handler, version: "5.4.0");

        await sut.BulkDeleteByUidsAsync("invasion", "user1", [10, 11]);

        var onProfile = handler.Requests.Where(r => r.Url.EndsWith("profile=2", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, onProfile.Count);
        Assert.Contains(onProfile, r => r.Url == $"{ApiAddress}/api/v2/humans/user1/tracking/invasion?silent=true&profile=2" && r.Body == """{"uids":[10]}""");
        Assert.Contains(onProfile, r => r.Url == $"{ApiAddress}/api/v2/humans/user1/tracking/incident?silent=true&profile=2" && r.Body == """{"uids":[11]}""");
    }

    [Fact]
    public async Task AQuickPickRemovedAfterAProfileSwitchDeletesItsAlarmsOnTheirProfile()
    {
        // The case that made this necessary. The pick was applied on profile 2; the scheduler has since made
        // profile 1 active. Remove deletes each tracked uid, and v2 alone would call them already gone, clear
        // the applied state, and leave the alarms firing.
        var handler = new ScriptedHandler
        {
            RouteReply = request =>
            {
                if (IsAllProfilesRead(request))
                {
                    return new Reply(HttpStatusCode.OK, AllProfiles("""{"pokemon":[{"uid":36486,"profile_no":2,"pokemon_id":25}]}"""));
                }

                return request.Url.EndsWith("profile=2", StringComparison.Ordinal)
                    ? new Reply(HttpStatusCode.OK, """{"deleted":[{"uid":36486}]}""")
                    : new Reply(HttpStatusCode.NotFound, RuleNotOnThisProfile, "application/problem+json");
            },
        };
        var proxy = CreateSut(handler, version: "5.4.0");

        var applied = new Mock<IQuickPickAppliedStateRepository>();
        applied.Setup(r => r.GetAsync("user1", 2, "pick"))
            .ReturnsAsync(new QuickPickAppliedState { UserId = "user1", ProfileNo = 2, QuickPickId = "pick", AlarmType = "monster", TrackedUids = [36486] });

        var quickPicks = new QuickPickService(
            Mock.Of<IQuickPickDefinitionRepository>(),
            applied.Object,
            new MonsterService(proxy, Mock.Of<IFeatureGate>(), Mock.Of<ITrackedUidRemapper>()),
            Mock.Of<IRaidService>(),
            Mock.Of<IEggService>(),
            Mock.Of<IQuestService>(),
            Mock.Of<IInvasionService>(),
            Mock.Of<ILureService>(),
            Mock.Of<INestService>(),
            Mock.Of<IGymService>(),
            Mock.Of<IMaxBattleService>(),
            Mock.Of<IMasterDataService>(),
            Mock.Of<IFeatureGate>(),
            Mock.Of<ILogger<QuickPickService>>());

        Assert.True(await quickPicks.RemoveAsync("user1", 2, "pick"));

        Assert.Contains(
            handler.Requests,
            r => r.Method == HttpMethod.Delete && r.Url == $"{ApiAddress}/api/v2/humans/user1/tracking/pokemon/36486?silent=true&profile=2");
    }

    [Fact]
    public async Task ABulkDeleteAServerOlderThan540RefusesForWantOfAUidQueryIsUnsupported()
    {
        // Before the body form, ?uid was required, so a 5.3 server answers the body with a 422 about it.
        var handler = ScriptedHandler.Problem(
            HttpStatusCode.UnprocessableEntity,
            """{"title":"Unprocessable Entity","status":422,"detail":"validation failed","errors":[{"message":"required query parameter is missing","location":"query.uid"}]}""");
        var sut = CreateSut(handler, version: "5.4.0");

        await Assert.ThrowsAsync<PoracleUnsupportedException>(() => sut.BulkDeleteByUidsAsync("pokemon", "user1", [1]));
    }

    [Fact]
    public async Task AnyOtherBulkDelete422IsStillAFailure()
    {
        var handler = ScriptedHandler.Problem(
            HttpStatusCode.UnprocessableEntity,
            """{"title":"Unprocessable Entity","status":422,"detail":"validation failed","errors":[{"message":"unknown query parameter","location":"query.profile"}]}""");
        var sut = CreateSut(handler, version: "5.4.0");

        var ex = await Assert.ThrowsAsync<PoracleRequestRefusedException>(() => sut.BulkDeleteByUidsAsync("pokemon", "user1", [1]));
        Assert.Equal(422, ex.StatusCode);
    }

    [Fact]
    public async Task AnIncidentDeleteNeverRoutesAPlainInvasionRowThatSharesItsUid()
    {
        // The all-profiles read folds incident into invasion, so a same-numbered grunt rule on another
        // profile is in the snapshot. An incident delete must leave it alone.
        var handler = new ScriptedHandler(
            new Reply(
                HttpStatusCode.NotFound,
                """{"title":"Not Found","status":404,"detail":"incident rule 412 not found for this human"}""",
                "application/problem+json"),
            new Reply(HttpStatusCode.OK, AllProfiles("""{"invasion":[{"uid":412,"profile_no":3,"grunt_type":"water"}]}""")));
        var sut = CreateSut(handler, version: "5.4.0");

        await sut.DeleteByUidAsync("incident", "user1", 412);

        Assert.Equal(2, handler.Requests.Count);
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Delete && r.Url.Contains("profile=3", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DeletingEveryAlarmOnAnAccountRemovesItsPokestopEventsToo()
    {
        // HumanService reads each type and bulk-deletes what it read. The invasion read hands it the event
        // rules as well; before the incident retry they would have been skipped by v2 and survived the
        // account's deletion.
        var handler = new ScriptedHandler()
        {
            Route = request =>
            {
                var path = request.Url;
                if (request.Method == HttpMethod.Get)
                {
                    if (path.Contains("/tracking/invasion?", StringComparison.Ordinal))
                    {
                        return """{"rules":[{"uid":10,"grunt_type":"water","gender":null,"distance":0,"clean":false}]}""";
                    }

                    if (path.Contains("/tracking/incident?", StringComparison.Ordinal))
                    {
                        return """{"rules":[{"uid":11,"display_type":8,"distance":0,"clean":false}]}""";
                    }

                    return """{"rules":[]}""";
                }

                if (path.Contains("/tracking/invasion?", StringComparison.Ordinal))
                {
                    return """{"deleted":[{"uid":10}]}""";
                }

                if (path.Contains("/tracking/incident?", StringComparison.Ordinal))
                {
                    return """{"deleted":[{"uid":11}]}""";
                }

                throw new InvalidOperationException($"unexpected {request.Method} {path}");
            },
        };

        var proxy = CreateSut(handler, version: "5.4.0");
        var humans = new HumanService(Mock.Of<IPoracleHumanProxy>(), proxy);

        var count = await humans.DeleteAllAlarmsByUserAsync("user1");

        Assert.Equal(2, count);
        var deletes = handler.Requests.Where(r => r.Method == HttpMethod.Delete).ToList();
        Assert.Equal(2, deletes.Count);
        Assert.Equal($"{ApiAddress}/api/v2/humans/user1/tracking/invasion?silent=true", deletes[0].Url);
        Assert.Equal("""{"uids":[10,11]}""", deletes[0].Body);
        Assert.Equal($"{ApiAddress}/api/v2/humans/user1/tracking/incident?silent=true", deletes[1].Url);
        Assert.Equal("""{"uids":[11]}""", deletes[1].Body);
    }

    // ---- carrying what the read kept but this build does not model -----------------------------------
    //
    // The v2 read copies an unmodelled property through verbatim (the PGAN fork's personal_shiny and
    // personal_background, 1,111 production rules), TrackingFieldPreserver carries it onto the edit, and
    // the write must then send it back. It may, exactly when the server's own /openapi.json declares that
    // property on the type's V2*Rule: every rule is additionalProperties:false, so a property the server
    // does not declare is still a row v2 cannot be told faithfully.

    private const string PersonalShinyRead = """
        {"rules":[{"uid":36486,"pokemon_id":25,"min_iv":90,"distance":0,"clean":true,"template":"1",
                   "personal_shiny":true,"description":"**Pikachu**"}]}
        """;

    /// <summary>An edit as MonsterService builds it: the modelled fields only.</summary>
    private const string PersonalShinyEdit = """
        {"uid":36486,"id":"user1","pokemon_id":25,"min_iv":95,"distance":0,"clean":1,"template":"1"}
        """;

    [Fact]
    public async Task AnEditCarriesAPersonalShinyTheServerDeclaresOntoTheV2Put()
    {
        var handler = new ScriptedHandler(
            new Reply(HttpStatusCode.OK, PersonalShinyRead),
            new Reply(HttpStatusCode.OK, """{"updated":[{"uid":36486}]}"""))
        {
            OpenApi = OpenApiDeclaring("V2PokemonRule", "personal_shiny", "boolean"),
        };
        var sut = CreateSut(handler, version: "5.4.0");

        var body = await TrackingFieldPreserver.PreserveStoredFieldsAsync(sut, "pokemon", "user1", 36486, Row(PersonalShinyEdit));
        var result = await sut.TryReplaceV2Async("pokemon", "user1", 36486, body);

        Assert.NotNull(result);
        Assert.True(result.Value.UsedV2);
        var put = handler.Requests[^1];
        Assert.Equal(HttpMethod.Put, put.Method);
        Assert.Equal($"{ApiAddress}/api/v2/humans/user1/tracking/pokemon/36486?silent=true", put.Url);
        using var sent = JsonDocument.Parse(put.Body!);
        Assert.True(sent.RootElement.GetProperty("personal_shiny").GetBoolean());
        Assert.Equal(95, sent.RootElement.GetProperty("min_iv").GetInt32());
    }

    [Fact]
    public async Task AnUnmodelledPropertyTheServerDoesNotDeclareStillKeepsTheRowOffV2()
    {
        // The other half: the plain 5.4.0 schema has no personal_shiny, so sending it would be a 422. The
        // row declines exactly as it did before the carry-through existed.
        var handler = new ScriptedHandler(new Reply(HttpStatusCode.OK, PersonalShinyRead))
        {
            OpenApi = File.ReadAllText(Fixture("poracleng-openapi-5.4.0.json")),
        };
        var sut = CreateSut(handler, version: "5.4.0");

        var body = await TrackingFieldPreserver.PreserveStoredFieldsAsync(sut, "pokemon", "user1", 36486, Row(PersonalShinyEdit));
        var result = await sut.TryReplaceV2Async("pokemon", "user1", 36486, body);

        Assert.Null(result);
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Put);
    }

    [Fact]
    public async Task ADeclaredPropertyOnAnotherTypeDoesNotLetItThrough()
    {
        // Declared per rule schema: personal_shiny on V2RaidRule says nothing about V2PokemonRule.
        var handler = new ScriptedHandler(new Reply(HttpStatusCode.OK, PersonalShinyRead))
        {
            OpenApi = OpenApiDeclaring("V2RaidRule", "personal_shiny", "boolean"),
        };
        var sut = CreateSut(handler, version: "5.4.0");

        var body = await TrackingFieldPreserver.PreserveStoredFieldsAsync(sut, "pokemon", "user1", 36486, Row(PersonalShinyEdit));

        Assert.Null(await sut.TryReplaceV2Async("pokemon", "user1", 36486, body));
    }

    /// <summary>The real 5.4.0 schema with one extra property declared on one rule.</summary>
    private static string OpenApiDeclaring(string schema, string property, string type)
    {
        var document = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Fixture("poracleng-openapi-5.4.0.json")))!;
        document["components"]!["schemas"]![schema]!["properties"]![property] =
            new System.Text.Json.Nodes.JsonObject { ["type"] = type };
        return document.ToJsonString();
    }

    private static string Fixture(string name, [System.Runtime.CompilerServices.CallerFilePath] string? here = null) =>
        Path.Combine(Path.GetDirectoryName(here)!, "..", "Fixtures", name);

    // ---- invasion (#841) ------------------------------------------------------------------------
    //
    // Two questions the server answers rather than this code assuming: whether its v2 invasion rule
    // carries grunt_type at all, and whether it knows the particular name a row holds. The second is not
    // belt-and-braces — 32 of 201 invasion rules in production carry a name v2 refuses, and all 32 are
    // visible and editable in the invasion list because v1's read returns them where v2's does not.

    private const string InvasionRow = """
        {"uid":412,"id":"user1","profile_no":1,"grunt_type":"water","gender":1,"distance":500,
         "clean":0,"template":"","ping":"","description":"**Water**"}
        """;

    private static PoracleV2Capabilities WithGruntType() =>
        new() { Read = true, InvasionGruntType = true };

    [Fact]
    public async Task AnInvasionEditUsesV2WhenTheServerCarriesGruntTypeAndKnowsTheName()
    {
        var handler = ScriptedHandler.Ok("""{"created":null,"updated":[{"grunt_type":"water","uid":413}],"unchanged":null}""");
        var sut = CreateSut(handler, version: "5.4.0", capabilities: WithGruntType(), gruntNames: ["water"]);

        var result = await sut.UpdateByUidAsync("invasion", "user1", 412, Row(InvasionRow));

        Assert.True(result.UsedV2);
        Assert.Equal(413, result.Uid);
        var request = Assert.Single(handler.Requests);
        Assert.Equal($"{ApiAddress}/api/v2/humans/user1/tracking/invasion/412?silent=true", request.Url);

        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal("water", body.RootElement.GetProperty("grunt_type").GetString());
        Assert.Equal("male", body.RootElement.GetProperty("gender").GetString());
    }

    [Fact]
    public async Task AnInvasionEditStaysOnV1WhenTheServerHasNoGruntType()
    {
        // 5.2.1. Its v2 invasion rule offers type_id, grunt_id, boss and everything — none of which can
        // express a grunt name — so there is nothing faithful to send.
        var handler = new ScriptedHandler(
            new Reply(HttpStatusCode.OK, """{"newUids":[412],"alreadyPresent":0,"updates":1,"insert":0}"""));
        var sut = CreateSut(handler, version: "5.4.0", capabilities: PoracleV2Capabilities.None, gruntNames: ["water"]);

        var result = await sut.UpdateByUidAsync("invasion", "user1", 412, Row(InvasionRow));

        Assert.False(result.UsedV2);
        Assert.Equal($"{ApiAddress}/api/tracking/invasion/user1?silent=true", Assert.Single(handler.Requests).Url);
    }

    [Theory]
    [InlineData("kecleon")]
    [InlineData("gold-stop")]
    [InlineData("showcase")]
    [InlineData("metal")]
    public async Task AnInvasionEditStaysOnV1ForANameTheServerDoesNotKnow(string gruntType)
    {
        // The four values production actually holds that v2 refuses. kecleon, gold-stop and showcase are
        // pokestop events and belong to /incident; metal is one this application wrote itself, from a
        // list that says metal where the game data says steel. A 422 here would fail an edit on a rule
        // the user did not break.
        var handler = new ScriptedHandler(
            new Reply(HttpStatusCode.OK, """{"newUids":[412],"alreadyPresent":0,"updates":1,"insert":0}"""));
        var sut = CreateSut(
            handler, version: "5.4.0", capabilities: WithGruntType(), gruntNames: ["water", "steel", "giovanni"]);

        var row = InvasionRow.Replace("\"water\"", $"\"{gruntType}\"", StringComparison.Ordinal);
        var result = await sut.UpdateByUidAsync("invasion", "user1", 412, Row(row));

        Assert.False(result.UsedV2);
        Assert.Equal(HttpMethod.Post, Assert.Single(handler.Requests).Method);
    }

    [Theory]
    [InlineData("player team leader", "player_team_leader")]
    [InlineData("npc 0", "npc_0")]
    public async Task ASpacedGruntNameIsMatchedInTheFormTheServerStoresIt(string stored, string known)
    {
        // Ten production rows hold the spaced form. v2 normalises them on write — verified against a live
        // build — so they are sendable, and matching has to normalise the same way or they would be sent
        // to v1 for no reason at all.
        var handler = ScriptedHandler.Ok("""{"updated":[{"uid":413}]}""");
        var sut = CreateSut(handler, version: "5.4.0", capabilities: WithGruntType(), gruntNames: [known]);

        var row = InvasionRow.Replace("\"water\"", $"\"{stored}\"", StringComparison.Ordinal);
        var result = await sut.UpdateByUidAsync("invasion", "user1", 412, Row(row));

        Assert.True(result.UsedV2);
    }

    [Fact]
    public async Task AnInvasionEditStaysOnV1WhenTheGruntListCouldNotBeRead()
    {
        // Fails closed: an empty list is how an unreachable masterdata read answers, and every invasion
        // write goes to v1 today anyway, so it is a no-change rather than a failure.
        var handler = new ScriptedHandler(
            new Reply(HttpStatusCode.OK, """{"newUids":[412],"alreadyPresent":0,"updates":1,"insert":0}"""));
        var sut = CreateSut(handler, version: "5.4.0", capabilities: WithGruntType(), gruntNames: []);

        Assert.False((await sut.UpdateByUidAsync("invasion", "user1", 412, Row(InvasionRow))).UsedV2);
    }

    [Fact]
    public async Task TheGruntNameGateDoesNotTouchTheOtherNineTypes()
    {
        // The check is keyed on the type. A pokemon edit must not start consulting a grunt list.
        var handler = ScriptedHandler.Ok(RotatedOk);
        var sut = CreateSut(handler, version: "5.4.0", capabilities: PoracleV2Capabilities.None, gruntNames: []);

        Assert.True((await sut.UpdateByUidAsync("pokemon", "user1", 36486, Row(StoredRow))).UsedV2);
    }

    [Fact]
    public async Task ARowWithAPingRidesV2OnTheOtherNineTypesEither()
    {
        var handler = ScriptedHandler.Ok("""{"newUids":[412],"alreadyPresent":0,"updates":1,"insert":0}""");
        var sut = CreateSut(handler, version: "5.4.0", capabilities: WithGruntType(), gruntNames: ["water"]);

        var row = InvasionRow.Replace("\"ping\":\"\"", "\"ping\":\"<@&400027130022592512>\"", StringComparison.Ordinal);

        await sut.UpdateByUidAsync("invasion", "user1", 412, Row(row));

        var request = Assert.Single(handler.Requests);
        Assert.Contains("/api/v2/humans/user1/tracking/invasion/412", request.Url, StringComparison.Ordinal);
        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal("<@&400027130022592512>", body.RootElement.GetProperty("ping").GetString());
    }

    [Fact]
    public async Task ABulkDeleteOnAProfileSendsProfileAndTheUidsAndReturnsWhatWasDeleted()
    {
        // The answer lists only the rules PoracleNG removed: it skips a uid that is not on that profile
        // without complaint, so the caller learns what happened from this list and nowhere else.
        var handler = ScriptedHandler.Ok("""{"deleted":[{"uid":672},{"uid":673}],"dry_run":false}""");
        var sut = CreateSut(handler, version: "5.4.0");

        var deleted = await sut.BulkDeleteByUidsOnProfileAsync("raid", "user1", [672, 673, 999], 2);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.Equal($"{ApiAddress}/api/v2/humans/user1/tracking/raid?silent=true&profile=2", request.Url);
        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal([672, 673, 999], body.RootElement.GetProperty("uids").EnumerateArray().Select(e => e.GetInt32()));
        Assert.Equal([672, 673], deleted);
    }

    [Fact]
    public async Task ABulkDeleteOnAProfileWithNothingToDeleteSendsNothing()
    {
        var handler = ScriptedHandler.Ok("""{"deleted":[]}""");

        var deleted = await CreateSut(handler, version: "5.4.0").BulkDeleteByUidsOnProfileAsync("raid", "user1", [], 2);

        Assert.Empty(deleted);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ABulkDeleteOnAProfileReadsNullDeletedAsNone()
    {
        var deleted = await CreateSut(ScriptedHandler.Ok("""{"deleted":null}"""), version: "5.4.0")
            .BulkDeleteByUidsOnProfileAsync("raid", "user1", [1], 2);

        Assert.Empty(deleted);
    }

    [Fact]
    public async Task ABulkDeleteOnAProfileReportsAServerWithoutTheRouteAsUnsupported()
    {
        var handler = new ScriptedHandler(new Reply(HttpStatusCode.NotFound, "404 page not found", "text/plain"));

        await Assert.ThrowsAsync<PoracleUnsupportedException>(
            () => CreateSut(handler, version: "5.4.0").BulkDeleteByUidsOnProfileAsync("raid", "user1", [1], 2));
    }

    [Fact]
    public async Task ABulkDeleteOnAProfileCarriesPoracleNgsDetailOnARefusal()
    {
        var handler = ScriptedHandler.Problem(
            HttpStatusCode.UnprocessableEntity, """{"title":"Unprocessable Entity","status":422,"detail":"unknown profile"}""");

        var ex = await Assert.ThrowsAsync<PoracleRequestRefusedException>(
            () => CreateSut(handler, version: "5.4.0").BulkDeleteByUidsOnProfileAsync("raid", "user1", [1], 2));

        Assert.Contains("unknown profile", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ABulkDeleteOnAProfileTreatsAMissingHumanAsAGoneAccount()
    {
        var handler = ScriptedHandler.Problem(
            HttpStatusCode.NotFound, """{"title":"Not Found","status":404,"detail":"human not found"}""");

        await Assert.ThrowsAsync<AccountGoneException>(
            () => CreateSut(handler, version: "5.4.0").BulkDeleteByUidsOnProfileAsync("raid", "user1", [1], 2));
    }

    private static JsonElement Row(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static PoracleTrackingProxy CreateSut(
        ScriptedHandler handler,
        string? version,
        string trackingApiVersion = "auto",
        IMemoryCache? cache = null,
        PoracleV2Capabilities? capabilities = null,
        string[]? gruntNames = null,
        ILogger<PoracleTrackingProxy>? logger = null)
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
            PoracleTrackingProxyTests.V2Schema(capabilities),
            PoracleTrackingProxyTests.GruntNames(gruntNames ?? []),
            cache ?? new MemoryCache(new MemoryCacheOptions()),
            logger ?? Mock.Of<ILogger<PoracleTrackingProxy>>());
    }

    private sealed record Reply(HttpStatusCode Status, string Body, string ContentType = "application/json");

    private sealed record Sent(HttpMethod Method, string Url, string? Body);

    /// <summary>Answers a fixed sequence of replies and records everything that was sent.</summary>
    private sealed class ScriptedHandler(params Reply[] replies) : HttpMessageHandler
    {
        private int _next;

        public List<Sent> Requests { get; } = [];

        /// <summary>
        /// What this server declares about its own v2 rules. The proxy reads it before every translation
        /// to learn which filter values that instance will refuse, so it is answered out of band: it is
        /// not part of the scripted sequence and is not recorded, because no test here is about it.
        /// A server declaring nothing is the 5.2.1 case, and the default.
        /// </summary>
        public string OpenApi { get; init; } = @"{""components"":{""schemas"":{}}}";

        /// <summary>When set, answers every request 200 with whatever this returns, instead of the script.</summary>
        public Func<Sent, string>? Route { get; init; }

        /// <summary>When set, answers every request with the reply this returns, instead of the script.</summary>
        public Func<Sent, Reply>? RouteReply { get; init; }

        public static ScriptedHandler Ok(string body) => new(new Reply(HttpStatusCode.OK, body));

        public static ScriptedHandler Problem(HttpStatusCode status, string body) =>
            new(new Reply(status, body, "application/problem+json"));

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/openapi.json", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(this.OpenApi, Encoding.UTF8, "application/json"),
                };
            }

            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            var sent = new Sent(request.Method, request.RequestUri.ToString(), body);
            this.Requests.Add(sent);

            if (this.RouteReply is { } routeReply)
            {
                var routed = routeReply(sent);
                return new HttpResponseMessage(routed.Status)
                {
                    Content = new StringContent(routed.Body, Encoding.UTF8, routed.ContentType),
                };
            }

            if (this.Route is { } route)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(route(sent), Encoding.UTF8, "application/json"),
                };
            }

            var reply = replies[Math.Min(this._next++, replies.Length - 1)];
            return new HttpResponseMessage(reply.Status)
            {
                Content = new StringContent(reply.Body, Encoding.UTF8, reply.ContentType),
            };
        }
    }

    /// <summary>Records every log line the proxy writes, at every level.</summary>
    private sealed class CapturingLogger : ILogger<PoracleTrackingProxy>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            this.Entries.Add((logLevel, formatter(state, exception)));
    }
}
