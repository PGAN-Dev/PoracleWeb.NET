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
/// Tracking creates through PoracleNG's <c>/api/v2</c> POST: a dry run first, then a commit of only the rows
/// the dry run reports as new. v2's POST still diffs and merges (a row one updatable field away from a stored
/// rule updates that rule), so the dry run is what keeps an Add from taking another alarm over.
/// </summary>
/// <remarks>
/// PoracleNG classifies each submitted row against stored rules only, never against the other rows of the
/// same batch (<c>store.RowDiffPolicy.Classify</c>), and its buckets carry no index back to the request. That
/// is why a mixed batch is re-classified one row at a time.
/// </remarks>
public class TrackingCreateProtocolTests
{
    private const string ApiAddress = "http://localhost:3030";
    private const string Base = $"{ApiAddress}/api/v2/humans/user1/tracking/pokemon";
    private const string TakeoverMessage = "Another alarm of this type already uses those settings. Edit or remove that one instead.";

    private readonly Mock<IUserAreaPolicy> _policy = new();

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static string Envelope(bool dryRun, string created = "null", string updated = "null", string unchanged = "null") =>
        $$"""{"dry_run":{{(dryRun ? "true" : "false")}},"created":{{created}},"updated":{{updated}},"unchanged":{{unchanged}}}""";

    private static bool IsDryRun(Sent sent) => sent.Url.Contains("dry_run=true", StringComparison.Ordinal);

    /// <summary>The pokemon ids in a request body, which is always an array of rules on the create route.</summary>
    private static List<int> PokemonIds(Sent sent)
    {
        using var body = JsonDocument.Parse(sent.Body!);
        Assert.Equal(JsonValueKind.Array, body.RootElement.ValueKind);
        return body.RootElement.EnumerateArray().Select(r => r.GetProperty("pokemon_id").GetInt32()).ToList();
    }

    // ---- single rule --------------------------------------------------------------------------------

    [Fact]
    public async Task SingleCreateAllNewDryRunThenCommitReturnsUid()
    {
        var handler = new Handler(sent => IsDryRun(sent)
            ? Reply.Ok(Envelope(true, created: """[{"uid":0,"pokemon_id":25}]"""))
            : Reply.Ok(Envelope(false, created: """[{"uid":501,"pokemon_id":25}]""")));

        var result = await this.CreateSut(handler).CreateAsync(
            "pokemon", "user1", Json("""{"uid":0,"id":"user1","profile_no":1,"pokemon_id":25,"min_iv":90,"description":"x"}"""));

        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, r => Assert.Equal(HttpMethod.Post, r.Method));
        Assert.Equal($"{Base}?silent=true&dry_run=true", handler.Requests[0].Url);
        Assert.Equal($"{Base}?silent=true", handler.Requests[1].Url);

        foreach (var request in handler.Requests)
        {
            using var body = JsonDocument.Parse(request.Body!);
            var rule = Assert.Single(body.RootElement.EnumerateArray());
            Assert.Equal(25, rule.GetProperty("pokemon_id").GetInt32());
            Assert.Equal(90, rule.GetProperty("min_iv").GetInt32());
            foreach (var name in new[] { "uid", "id", "profile_no", "description" })
            {
                Assert.False(rule.TryGetProperty(name, out _), $"{name} must not reach /api/v2");
            }
        }

        Assert.Equal([501], result.RowUids);
        Assert.Equal([501L], result.NewUids);
        Assert.Equal(1, result.Inserts);
        Assert.Equal(0, result.Updates);
        Assert.Equal(0, result.AlreadyPresent);
        Assert.Equal(501, result.PrimaryUid);
    }

    [Fact]
    public async Task SingleCreateWouldTakeOverRefusesWithMessageNoCommit()
    {
        // Same pokemon, only min_iv differs from a stored rule: PoracleNG would update that rule in place.
        var handler = new Handler(_ => Reply.Ok(Envelope(
            true,
            updated: """[{"uid":77,"pokemon_id":25,"min_iv":95,"previous":{"uid":77,"pokemon_id":25,"min_iv":90}}]""")));

        var ex = await Assert.ThrowsAsync<TrackingConflictException>(() => this.CreateSut(handler).CreateAsync(
            "pokemon", "user1", Json("""{"pokemon_id":25,"min_iv":95}""")));

        Assert.Equal(TakeoverMessage, ex.Message);
        Assert.Equal("pokemon", ex.TrackingType);
        Assert.Equal(77, ex.ExistingUid);
        Assert.True(IsDryRun(Assert.Single(handler.Requests)));
    }

    [Fact]
    public async Task SingleCreateTakeoverNamesTheStoredRuleFromPrevious()
    {
        // previous is the stored rule as it is now; its uid is the one the user would edit.
        var handler = new Handler(_ => Reply.Ok(Envelope(
            true,
            updated: """[{"uid":0,"pokemon_id":25,"min_iv":95,"previous":{"uid":81,"pokemon_id":25,"min_iv":90}}]""")));

        var ex = await Assert.ThrowsAsync<TrackingConflictException>(() => this.CreateSut(handler).CreateAsync(
            "pokemon", "user1", Json("""{"pokemon_id":25,"min_iv":95}""")));

        Assert.Equal(81, ex.ExistingUid);
    }

    [Fact]
    public async Task SingleCreateIdenticalReturnsAlreadyPresentNoCommit()
    {
        var handler = new Handler(_ => Reply.Ok(Envelope(true, unchanged: """[{"uid":77,"pokemon_id":25}]""")));

        var result = await this.CreateSut(handler).CreateAsync("pokemon", "user1", Json("""{"pokemon_id":25}"""));

        Assert.True(IsDryRun(Assert.Single(handler.Requests)));
        Assert.Equal(1, result.AlreadyPresent);
        Assert.Equal([0], result.RowUids);
        Assert.Empty(result.NewUids);
        Assert.Equal(0, result.Inserts);
        Assert.True(result.WasRejectedAsDuplicate);
        // The stored rule the Add matched, so a type that refuses an exact repeat can name it.
        Assert.Equal(77, result.ExistingUid);
    }

    [Fact]
    public async Task SingleCreateTwoUpdatableDifferencesDryRunSaysCreatedCommits()
    {
        // The legitimate case beside the takeover refusal: a rule two updatable fields (distance and
        // template) away from a stored one is a separate rule, and PoracleNG's dry run says so.
        var handler = new Handler(sent => IsDryRun(sent)
            ? Reply.Ok(Envelope(true, created: """[{"uid":0,"pokemon_id":25}]"""))
            : Reply.Ok(Envelope(false, created: """[{"uid":502,"pokemon_id":25}]""")));

        var result = await this.CreateSut(handler).CreateAsync(
            "pokemon", "user1", Json("""{"pokemon_id":25,"distance":1500,"template":"2"}"""));

        Assert.Equal(2, handler.Requests.Count);
        Assert.False(IsDryRun(handler.Requests[1]));
        Assert.Equal([502], result.RowUids);
        Assert.Equal(1, result.Inserts);
    }

    [Fact]
    public async Task CommitReportsUpdatedRestoresPreviousThenRefuses()
    {
        // The dry run said new, but by the commit another write had made it a takeover. The rule PoracleNG
        // overwrote is put back as it was before the user is told.
        var handler = new Handler(sent => sent.Method == HttpMethod.Put
            ? Reply.Ok(Envelope(false, updated: """[{"uid":77,"pokemon_id":25,"min_iv":90}]"""))
            : IsDryRun(sent)
                ? Reply.Ok(Envelope(true, created: """[{"uid":0,"pokemon_id":25}]"""))
                : Reply.Ok(Envelope(
                    false,
                    updated: """[{"uid":77,"pokemon_id":25,"min_iv":95,"previous":{"uid":77,"pokemon_id":25,"min_iv":90,"clean":true,"distance":0}}]""")));

        var ex = await Assert.ThrowsAsync<TrackingConflictException>(() => this.CreateSut(handler).CreateAsync(
            "pokemon", "user1", Json("""{"pokemon_id":25,"min_iv":95}""")));

        Assert.Equal(TakeoverMessage, ex.Message);
        Assert.Equal(77, ex.ExistingUid);
        Assert.Equal(3, handler.Requests.Count);

        var restore = handler.Requests[2];
        Assert.Equal(HttpMethod.Put, restore.Method);
        Assert.Equal($"{Base}/77?silent=true&trusted=true", restore.Url);
        using var body = JsonDocument.Parse(restore.Body!);
        Assert.Equal(JsonValueKind.Object, body.RootElement.ValueKind);
        Assert.Equal(25, body.RootElement.GetProperty("pokemon_id").GetInt32());
        Assert.Equal(90, body.RootElement.GetProperty("min_iv").GetInt32());
        Assert.True(body.RootElement.GetProperty("clean").GetBoolean());
        Assert.False(body.RootElement.TryGetProperty("uid", out _));
    }

    // ---- batches ------------------------------------------------------------------------------------

    [Fact]
    public async Task BatchAllNewRowUidsInOrder()
    {
        var handler = new Handler(sent => IsDryRun(sent)
            ? Reply.Ok(Envelope(true, created: """[{"uid":0},{"uid":0},{"uid":0}]"""))
            : Reply.Ok(Envelope(false, created: """[{"uid":11},{"uid":12},{"uid":13}]""")));

        var result = await this.CreateSut(handler).CreateAsync(
            "pokemon", "user1", Json("""[{"pokemon_id":25},{"pokemon_id":26},{"pokemon_id":27}]"""));

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal([25, 26, 27], PokemonIds(handler.Requests[1]));
        Assert.Equal([11, 12, 13], result.RowUids);
        Assert.Equal([11L, 12L, 13L], result.NewUids);
        Assert.Equal(3, result.Inserts);
        Assert.Equal(0, result.TakeoversSkipped);
    }

    [Fact]
    public async Task BatchMixedPerRowDryRunsCommitsOnlyNewRowUidsZeroForOthersTakeoversSkippedCounted()
    {
        // 25 is new, 26 would take a stored rule over, 27 is already stored exactly. The batch dry run says
        // so but cannot say which row is which, so each row is asked about alone.
        var handler = new Handler(sent =>
        {
            if (!IsDryRun(sent))
            {
                return Reply.Ok(Envelope(false, created: """[{"uid":11,"pokemon_id":25}]"""));
            }

            var ids = PokemonIds(sent);
            if (ids.Count > 1)
            {
                return Reply.Ok(Envelope(
                    true,
                    created: """[{"uid":0,"pokemon_id":25}]""",
                    updated: """[{"uid":40,"pokemon_id":26,"previous":{"uid":40,"pokemon_id":26}}]""",
                    unchanged: """[{"uid":41,"pokemon_id":27}]"""));
            }

            return ids[0] switch
            {
                25 => Reply.Ok(Envelope(true, created: """[{"uid":0,"pokemon_id":25}]""")),
                26 => Reply.Ok(Envelope(true, updated: """[{"uid":40,"pokemon_id":26,"previous":{"uid":40,"pokemon_id":26}}]""")),
                _ => Reply.Ok(Envelope(true, unchanged: """[{"uid":41,"pokemon_id":27}]""")),
            };
        });

        var result = await this.CreateSut(handler).CreateAsync(
            "pokemon", "user1", Json("""[{"pokemon_id":25},{"pokemon_id":26},{"pokemon_id":27}]"""));

        Assert.Equal(5, handler.Requests.Count);
        Assert.Equal([25, 26, 27], PokemonIds(handler.Requests[0]));
        Assert.Equal([[25], [26], [27]], handler.Requests.Skip(1).Take(3).Select(PokemonIds));
        Assert.All(handler.Requests.Take(4), r => Assert.True(IsDryRun(r)));

        var commit = handler.Requests[4];
        Assert.False(IsDryRun(commit));
        Assert.Equal([25], PokemonIds(commit));

        Assert.Equal([11, 0, 0], result.RowUids);
        Assert.Equal(1, result.AlreadyPresent);
        Assert.Equal(1, result.TakeoversSkipped);
        Assert.Equal(1, result.Inserts);
        Assert.Equal(0, result.Updates);
    }

    [Fact]
    public async Task BatchMixedHalvesInsteadOfAskingAboutEveryRow()
    {
        // Selecting every species in the picker and adding them, while already holding one of them and one rule a
        // new row would take over. Asking about each of the 64 rows alone cost 64 dry runs; halving the mixed
        // groups finds the two in a few.
        const int Stored = 120;
        const int Takeover = 150;
        var handler = new Handler(sent =>
        {
            var ids = PokemonIds(sent);
            if (!IsDryRun(sent))
            {
                return Reply.Ok(Envelope(false, created: Rules(ids, id => 1000 + id)));
            }

            return Reply.Ok(Envelope(
                true,
                created: Rules(ids.Where(id => id is not Stored and not Takeover), _ => 0),
                updated: ids.Contains(Takeover) ? """[{"uid":40,"pokemon_id":150,"previous":{"uid":40,"pokemon_id":150}}]""" : "null",
                unchanged: ids.Contains(Stored) ? """[{"uid":41,"pokemon_id":120}]""" : "null"));
        });

        var species = Enumerable.Range(101, 64).ToList();
        var result = await this.CreateSut(handler).CreateAsync(
            "pokemon", "user1", Json(JsonSerializer.Serialize(species.Select(id => new { pokemon_id = id }))));

        var dryRuns = handler.Requests.Count(IsDryRun);
        Assert.InRange(dryRuns, 1, 25);

        var commit = Assert.Single(handler.Requests, r => !IsDryRun(r));
        Assert.Equal(species.Where(id => id is not Stored and not Takeover), PokemonIds(commit));

        Assert.Equal(species.Select(id => id is Stored or Takeover ? 0 : 1000 + id), result.RowUids);
        Assert.Equal(1, result.AlreadyPresent);
        Assert.Equal(1, result.TakeoversSkipped);
        Assert.Equal(62, result.Inserts);
    }

    private static string Rules(IEnumerable<int> ids, Func<int, int> uid) =>
        JsonSerializer.Serialize(ids.Select(id => new { uid = uid(id), pokemon_id = id }));

    [Fact]
    public async Task BatchNothingNewNoCommit()
    {
        var handler = new Handler(_ => Reply.Ok(Envelope(true, unchanged: """[{"uid":41}]""")));

        var result = await this.CreateSut(handler).CreateAsync(
            "pokemon", "user1", Json("""[{"pokemon_id":27}]"""));

        // One row: the batch dry run already classified it, so nothing more is asked.
        Assert.True(IsDryRun(Assert.Single(handler.Requests)));
        Assert.Equal([0], result.RowUids);
        Assert.Equal(1, result.AlreadyPresent);
    }

    [Fact]
    public async Task BatchAllExistingMakesNoPerRowRuns()
    {
        // Re-applying a quick pick of a thousand rules that are all stored already: the batch dry run creates
        // nothing, so nothing would be written and there is nothing to tell apart row by row. Asking about each
        // row alone cost one POST per row.
        var handler = new Handler(_ => Reply.Ok(Envelope(
            true,
            updated: """[{"uid":40,"pokemon_id":26,"previous":{"uid":40,"pokemon_id":26}}]""",
            unchanged: """[{"uid":41,"pokemon_id":25},{"uid":42,"pokemon_id":27}]""")));

        var result = await this.CreateSut(handler).CreateAsync(
            "pokemon", "user1", Json("""[{"pokemon_id":25},{"pokemon_id":26},{"pokemon_id":27}]"""));

        Assert.True(IsDryRun(Assert.Single(handler.Requests)));
        Assert.Equal([0, 0, 0], result.RowUids);
        Assert.Equal(2, result.AlreadyPresent);
        Assert.Equal(1, result.TakeoversSkipped);
        Assert.Equal(0, result.Inserts);
        Assert.Empty(result.NewUids);
    }

    [Fact]
    public async Task BatchCommitCountMismatchRowUidsAllZeroNewUidsKept()
    {
        var handler = new Handler(sent => IsDryRun(sent)
            ? Reply.Ok(Envelope(true, created: """[{"uid":0},{"uid":0}]"""))
            : Reply.Ok(Envelope(false, created: """[{"uid":11,"pokemon_id":25}]""", unchanged: """[{"uid":9,"pokemon_id":26}]""")));
        var logger = new CapturingLogger();

        var result = await this.CreateSut(handler, logger).CreateAsync(
            "pokemon", "user1", Json("""[{"pokemon_id":25},{"pokemon_id":26}]"""));

        Assert.Equal([0, 0], result.RowUids);
        Assert.Equal([11L], result.NewUids);
        Assert.Equal(1, result.Inserts);
        Assert.Equal(1, result.AlreadyPresent);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task SingleCreateMadeIdenticalBeforeTheCommitIsAlreadyPresentWithoutAWarning()
    {
        // The dry run said new; by the commit another write had stored the same rule. Nothing was created and
        // the one kept row is accounted for, so there is nothing to pair and nothing to warn about.
        var handler = new Handler(sent => IsDryRun(sent)
            ? Reply.Ok(Envelope(true, created: """[{"uid":0,"pokemon_id":25}]"""))
            : Reply.Ok(Envelope(false, unchanged: """[{"uid":77,"pokemon_id":25}]""")));
        var logger = new CapturingLogger();

        var result = await this.CreateSut(handler, logger).CreateAsync("pokemon", "user1", Json("""{"pokemon_id":25}"""));

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(1, result.AlreadyPresent);
        Assert.Equal([0], result.RowUids);
        Assert.Empty(result.NewUids);
        Assert.DoesNotContain(logger.Entries, e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task ProfileNoGoesInQueryNotBody()
    {
        var handler = new Handler(sent => IsDryRun(sent)
            ? Reply.Ok(Envelope(true, created: """[{"uid":0}]"""))
            : Reply.Ok(Envelope(false, created: """[{"uid":31}]""")));

        var result = await this.CreateSut(handler).CreateOnProfileAsync(
            "pokemon", "user1", Json("""[{"pokemon_id":25,"profile_no":1}]"""), 3);

        Assert.Equal($"{Base}?silent=true&profile=3&dry_run=true", handler.Requests[0].Url);
        Assert.Equal($"{Base}?silent=true&profile=3", handler.Requests[1].Url);
        Assert.All(handler.Requests, r => Assert.DoesNotContain("profile_no", r.Body!, StringComparison.Ordinal));
        Assert.Equal([31], result.RowUids);
    }

    // ---- refusals before anything is sent --------------------------------------------------------------

    [Theory]
    [InlineData("""{"pokemon_id":25,"some_field_from_a_newer_poracle":4}""")]
    [InlineData("""[{"pokemon_id":25},{"pokemon_id":26,"gender":9}]""")]
    [InlineData("""{"distance":1000}""")]
    public async Task UntranslatableRowRefusedBeforeAnyRequest(string body)
    {
        var handler = new Handler(_ => Reply.Ok(Envelope(true)));

        var ex = await Assert.ThrowsAsync<AlarmValidationException>(
            () => this.CreateSut(handler).CreateAsync("pokemon", "user1", Json(body)));

        Assert.StartsWith("PoracleNG can't store this alarm as it is: ", ex.Message, StringComparison.Ordinal);
        Assert.EndsWith(". Nothing was changed.", ex.Message, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("""{"pokemon_id":25,"override_location_label":"work","override_areas":["town"],"distance":0}""", "An alarm can be limited to a place or to areas, not both.")]
    [InlineData("""{"pokemon_id":25,"override_areas":["town"],"distance":500}""", "An alarm limited to areas cannot also have a radius. Clear one of them.")]
    [InlineData("""{"pokemon_id":25,"override_location_label":"work","distance":0}""", "An alarm measured from a place needs a radius.")]
    public async Task IncoherentScopeRefusedBeforeAnyRequest(string body, string message)
    {
        var handler = new Handler(_ => Reply.Ok(Envelope(true)));

        var ex = await Assert.ThrowsAsync<AlarmValidationException>(
            () => this.CreateSut(handler).CreateAsync("pokemon", "user1", Json(body)));

        Assert.Equal(message, ex.Message);
        Assert.Empty(handler.Requests);
        this._policy.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task APlaceWithARadiusIsCoherentAndSent()
    {
        var handler = new Handler(sent => IsDryRun(sent)
            ? Reply.Ok(Envelope(true, created: """[{"uid":0}]"""))
            : Reply.Ok(Envelope(false, created: """[{"uid":5}]""")));

        await this.CreateSut(handler).CreateAsync(
            "pokemon", "user1", Json("""{"pokemon_id":25,"override_location_label":"work","distance":700}"""));

        Assert.Equal(2, handler.Requests.Count);
    }

    // ---- override areas -----------------------------------------------------------------------------

    [Fact]
    public async Task OwnFenceInOverrideAreasSendsTrusted()
    {
        this._policy
            .Setup(p => p.EnsureOverrideAreasAllowedAsync("user1", It.IsAny<IReadOnlyCollection<string>>(), null))
            .Returns(Task.CompletedTask);
        var handler = new Handler(sent => IsDryRun(sent)
            ? Reply.Ok(Envelope(true, created: """[{"uid":0}]"""))
            : Reply.Ok(Envelope(false, created: """[{"uid":6}]""")));

        await this.CreateSut(handler).CreateAsync(
            "pokemon", "user1", Json("""{"pokemon_id":25,"distance":0,"override_areas":["my park"]}"""));

        Assert.Equal($"{Base}?silent=true&trusted=true&dry_run=true", handler.Requests[0].Url);
        Assert.Equal($"{Base}?silent=true&trusted=true", handler.Requests[1].Url);
        Assert.Contains("\"my park\"", handler.Requests[1].Body!, StringComparison.Ordinal);
        this._policy.Verify(
            p => p.EnsureOverrideAreasAllowedAsync(
                "user1", It.Is<IReadOnlyCollection<string>>(names => names.Count == 1 && names.Contains("my park")), null),
            Times.Once);
    }

    [Fact]
    public async Task ABlankOverrideAreaNameDoesNotRideTheTrustedWrite()
    {
        this._policy
            .Setup(p => p.EnsureOverrideAreasAllowedAsync("user1", It.IsAny<IReadOnlyCollection<string>>(), null))
            .Returns(Task.CompletedTask);
        var handler = new Handler(sent => IsDryRun(sent)
            ? Reply.Ok(Envelope(true, created: """[{"uid":0}]"""))
            : Reply.Ok(Envelope(false, created: """[{"uid":6}]""")));

        await this.CreateSut(handler).CreateAsync(
            "pokemon", "user1", Json("""{"pokemon_id":25,"distance":0,"override_areas":["my park","  ",""]}"""));

        Assert.Equal($"{Base}?silent=true&trusted=true", handler.Requests[1].Url);
        foreach (var request in handler.Requests)
        {
            using var body = JsonDocument.Parse(request.Body!);
            var areas = body.RootElement[0].GetProperty("override_areas").EnumerateArray().Select(a => a.GetString()).ToList();
            Assert.Equal(["my park"], areas);
        }

        this._policy.Verify(
            p => p.EnsureOverrideAreasAllowedAsync(
                "user1", It.Is<IReadOnlyCollection<string>>(names => names.Count == 1 && names.Contains("my park")), null),
            Times.Once);
    }

    [Fact]
    public async Task OnlyBlankOverrideAreaNamesAreNoAreasAtAll()
    {
        var handler = new Handler(sent => IsDryRun(sent)
            ? Reply.Ok(Envelope(true, created: """[{"uid":0}]"""))
            : Reply.Ok(Envelope(false, created: """[{"uid":6}]""")));

        await this.CreateSut(handler).CreateAsync(
            "pokemon", "user1", Json("""{"pokemon_id":25,"distance":500,"override_areas":[" "]}"""));

        Assert.All(handler.Requests, r => Assert.DoesNotContain("trusted", r.Url, StringComparison.Ordinal));
        Assert.All(handler.Requests, r => Assert.DoesNotContain("\" \"", r.Body!, StringComparison.Ordinal));
        this._policy.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task NoOverrideAreasNoTrustedNoPolicyCall()
    {
        var handler = new Handler(sent => IsDryRun(sent)
            ? Reply.Ok(Envelope(true, created: """[{"uid":0},{"uid":0}]"""))
            : Reply.Ok(Envelope(false, created: """[{"uid":6},{"uid":7}]""")));

        await this.CreateSut(handler).CreateAsync(
            "pokemon", "user1", Json("""[{"pokemon_id":25,"override_areas":null},{"pokemon_id":26,"override_areas":[]}]"""));

        Assert.All(handler.Requests, r => Assert.DoesNotContain("trusted", r.Url, StringComparison.Ordinal));
        this._policy.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ForeignPrivateFenceRefusedNoRequest()
    {
        this._policy
            .Setup(p => p.EnsureOverrideAreasAllowedAsync("user1", It.IsAny<IReadOnlyCollection<string>>(), null))
            .ThrowsAsync(new AlarmValidationException("These areas can't be used on an alarm: someone else's park."));
        var handler = new Handler(_ => Reply.Ok(Envelope(true)));

        var ex = await Assert.ThrowsAsync<AlarmValidationException>(() => this.CreateSut(handler).CreateAsync(
            "pokemon", "user1", Json("""{"pokemon_id":25,"distance":0,"override_areas":["someone else's park"]}""")));

        Assert.Contains("someone else's park", ex.Message, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    // ---- max_areas (jfberry/PoracleNG#346) ----------------------------------------------------------

    [Fact]
    public async Task NewRowOverTheAreaLimitRefusedAfterTheDryRunNoCommit()
    {
        this._policy
            .Setup(p => p.EnsureOverrideAreasWithinLimitAsync("user1", It.IsAny<IReadOnlyCollection<string>>(), null))
            .ThrowsAsync(new AlarmValidationException("An alarm can be limited to at most 1 areas."));
        var handler = new Handler(_ => Reply.Ok(Envelope(true, created: """[{"uid":0,"pokemon_id":25}]""")));

        await Assert.ThrowsAsync<AlarmValidationException>(() => this.CreateSut(handler).CreateAsync(
            "pokemon", "user1", Json("""{"pokemon_id":25,"distance":0,"override_areas":["a","b"]}""")));

        Assert.All(handler.Requests, r => Assert.True(IsDryRun(r), "nothing may be committed"));
    }

    [Fact]
    public async Task RowAlreadyStoredUnchangedIsNotHeldToTheAreaLimit()
    {
        // Re-applying an alarm stored before max_areas was lowered writes nothing, so it must not be refused.
        this._policy
            .Setup(p => p.EnsureOverrideAreasWithinLimitAsync(It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), null))
            .ThrowsAsync(new AlarmValidationException("limit"));
        var handler = new Handler(_ => Reply.Ok(Envelope(true, unchanged: """[{"uid":0},{"uid":0}]""")));

        var result = await this.CreateSut(handler).CreateAsync(
            "pokemon", "user1", Json("""[{"pokemon_id":25,"distance":0,"override_areas":["a","b"]},{"pokemon_id":26,"distance":0,"override_areas":["a","b"]}]"""));

        Assert.Equal(2, result.AlreadyPresent);
    }

    [Fact]
    public async Task OnlyTheNewRowsOfABatchAreHeldToTheAreaLimit()
    {
        var handler = new Handler(sent =>
        {
            if (!IsDryRun(sent))
            {
                return Reply.Ok(Envelope(false, created: """[{"uid":9,"pokemon_id":26}]"""));
            }

            var ids = PokemonIds(sent);
            if (ids.Count == 2)
            {
                return Reply.Ok(Envelope(true, created: """[{"uid":0}]""", unchanged: """[{"uid":0}]"""));
            }

            return ids[0] == 25
                ? Reply.Ok(Envelope(true, unchanged: """[{"uid":0}]"""))
                : Reply.Ok(Envelope(true, created: """[{"uid":0}]"""));
        });

        await this.CreateSut(handler).CreateAsync(
            "pokemon", "user1", Json("""[{"pokemon_id":25,"distance":0,"override_areas":["a","b","c"]},{"pokemon_id":26,"distance":0,"override_areas":["d"]}]"""));

        this._policy.Verify(
            p => p.EnsureOverrideAreasWithinLimitAsync("user1", It.Is<IReadOnlyCollection<string>>(a => a.SequenceEqual(new[] { "d" })), null),
            Times.Once);
        this._policy.Verify(
            p => p.EnsureOverrideAreasWithinLimitAsync(
                It.IsAny<string>(), It.Is<IReadOnlyCollection<string>>(a => a.Contains("a")), It.IsAny<Func<Task<IReadOnlyCollection<string>>>?>()),
            Times.Never);
    }

    // ---- status mapping -----------------------------------------------------------------------------

    [Fact]
    public async Task PlaintextRoute404ThrowsUnsupported()
    {
        var handler = new Handler(_ => new Reply(HttpStatusCode.NotFound, "404 page not found", "text/plain"));

        var ex = await Assert.ThrowsAsync<PoracleUnsupportedException>(
            () => this.CreateSut(handler).CreateAsync("pokemon", "user1", Json("""{"pokemon_id":25}""")));

        Assert.Equal("saving alarms", ex.Feature);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task A422CarriesPoraclesExplanation()
    {
        var handler = new Handler(_ => new Reply(
            HttpStatusCode.UnprocessableEntity,
            """{"title":"Unprocessable Entity","status":422,"detail":"unknown grunt_type 'metal'"}""",
            "application/problem+json"));

        var ex = await Assert.ThrowsAsync<AlarmValidationException>(
            () => this.CreateSut(handler).CreateAsync("pokemon", "user1", Json("""{"pokemon_id":25}""")));

        Assert.Equal("unknown grunt_type 'metal'", ex.Message);
    }

    // ---- fixture ------------------------------------------------------------------------------------

    private PoracleTrackingProxy CreateSut(Handler handler, ILogger<PoracleTrackingProxy>? logger = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Poracle:ApiAddress"] = ApiAddress,
                ["Poracle:ApiSecret"] = "test-secret",
            })
            .Build();

        return new PoracleTrackingProxy(
            new HttpClient(handler),
            config,
            this._policy.Object,
            new MemoryCache(new MemoryCacheOptions()),
            logger ?? Mock.Of<ILogger<PoracleTrackingProxy>>());
    }

    private sealed record Reply(HttpStatusCode Status, string Body, string ContentType = "application/json")
    {
        public static Reply Ok(string body) => new(HttpStatusCode.OK, body);
    }

    private sealed record Sent(HttpMethod Method, string Url, string? Body);

    /// <summary>Answers each request from <c>route</c> and records it; <c>/openapi.json</c> is answered out of band.</summary>
    private sealed class Handler(Func<Sent, Reply> route) : HttpMessageHandler
    {
        public List<Sent> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/openapi.json", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"components":{"schemas":{}}}""", Encoding.UTF8, "application/json"),
                };
            }

            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            var sent = new Sent(request.Method, request.RequestUri.ToString(), body);
            this.Requests.Add(sent);

            var reply = route(sent);
            return new HttpResponseMessage(reply.Status)
            {
                Content = new StringContent(reply.Body, Encoding.UTF8, reply.ContentType),
            };
        }
    }

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
