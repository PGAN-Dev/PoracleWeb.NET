using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// Pokestop-event alarms. The behaviours asserted here were verified against a live PoracleNG,
/// not read off its source.
/// </summary>
/// <remarks>
/// Creates follow the same protocol as every other tracking type (<see cref="TrackingCreateProtocolTests"/>):
/// a dry run classifies each rule as created, updated (a takeover of another rule) or unchanged, and only
/// the created ones are committed. These tests pin the same classification on the incident side.
/// </remarks>
public class PokestopEventServiceTests
{
    private const string TakeoverMessage =
        "Another alarm of this type already uses those settings. Edit or remove that one instead.";

    private readonly Mock<IPoracleIncidentProxy> _proxy = new();
    private readonly Mock<IFeatureGate> _featureGate = new();
    private readonly PokestopEventService _sut;

    public PokestopEventServiceTests()
    {
        this._featureGate.Setup(g => g.EnsureEnabledAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        this._proxy.Setup(p => p.GetByUserAsync(It.IsAny<string>())).ReturnsAsync([]);
        this._sut = new PokestopEventService(this._proxy.Object, this._featureGate.Object, NullLogger<PokestopEventService>.Instance);
    }

    private static PokestopEvent Rule(int uid, int displayType, int distance = 100) => new()
    {
        Uid = uid,
        DisplayType = displayType,
        Distance = distance,
    };

    private void Existing(params PokestopEvent[] rules) =>
        this._proxy.Setup(p => p.GetByUserAsync("u1")).ReturnsAsync(rules);

    private static PokestopEventWriteResult Created(params PokestopEvent[] rules) => new(rules, [], []);

    private static PokestopEventWriteResult Updated(params PokestopEvent[] rules) => new([], rules, []);

    private static PokestopEventWriteResult Unchanged(params PokestopEvent[] rules) => new([], [], rules);

    /// <summary>Answers dry runs with <paramref name="dry"/> and the commit with <paramref name="commit"/>.</summary>
    private void CreateAnswers(Func<List<PokestopEvent>, PokestopEventWriteResult> dry, Func<List<PokestopEvent>, PokestopEventWriteResult> commit) =>
        this._proxy.Setup(p => p.CreateAsync("u1", It.IsAny<IEnumerable<PokestopEvent>>(), It.IsAny<bool>()))
            .ReturnsAsync((string _, IEnumerable<PokestopEvent> rules, bool dryRun) => dryRun ? dry([.. rules]) : commit([.. rules]));

    private void VerifyNoCommit() =>
        this._proxy.Verify(p => p.CreateAsync(It.IsAny<string>(), It.IsAny<IEnumerable<PokestopEvent>>(), false), Times.Never);

    // ── create ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsyncDryRunsThenCommitsAndAssignsTheUidPoracleNamed()
    {
        this.CreateAnswers(_ => Created(Rule(0, PokestopEventTypes.Showcase)), _ => Created(Rule(732, PokestopEventTypes.Showcase)));

        var result = await this._sut.CreateAsync("u1", Rule(0, PokestopEventTypes.Showcase));

        Assert.Equal(732, result.Uid);
        this._proxy.Verify(p => p.CreateAsync("u1", It.IsAny<IEnumerable<PokestopEvent>>(), true), Times.Once);
        this._proxy.Verify(p => p.CreateAsync("u1", It.IsAny<IEnumerable<PokestopEvent>>(), false), Times.Once);
    }

    /// <summary>
    /// One updatable field away from a stored rule: PoracleNG's create would rewrite that rule and re-key it
    /// (verified on 5.2.1: 732 became 733), answering as though something new had been made. The dry run
    /// reports it under <c>updated</c>, and nothing is committed.
    /// </summary>
    [Fact]
    public async Task PokestopEventCreateWouldTakeOverRefused()
    {
        this.CreateAnswers(_ => Updated(Rule(732, PokestopEventTypes.Showcase, distance: 900)), _ => throw new InvalidOperationException("no commit"));

        var ex = await Assert.ThrowsAsync<TrackingConflictException>(
            () => this._sut.CreateAsync("u1", Rule(0, PokestopEventTypes.Showcase, distance: 900)));

        Assert.Equal(TakeoverMessage, ex.Message);
        Assert.Equal(732, ex.ExistingUid);
        this.VerifyNoCommit();
    }

    /// <summary>
    /// The legitimate case beside the refusal: an area-scoped twin of a stored rule for the same event is a
    /// separate rule (verified on 5.2.1 and 5.3.0), so the dry run says created and it is inserted. The old
    /// hand-written diff had to know that; the dry run asks PoracleNG.
    /// </summary>
    [Fact]
    public async Task PokestopEventCreateAreaScopedTwinInserts()
    {
        this.Existing(Rule(732, PokestopEventTypes.Showcase, distance: 500));
        var twin = new PokestopEvent { DisplayType = PokestopEventTypes.Showcase, OverrideAreas = ["downtown"] };
        this.CreateAnswers(_ => Created(Rule(0, PokestopEventTypes.Showcase, 0)), _ => Created(Rule(740, PokestopEventTypes.Showcase, 0)));

        var result = await this._sut.CreateAsync("u1", twin);

        Assert.Equal(740, result.Uid);
    }

    [Fact]
    public async Task CreateAsyncSendsItsPingAndPlaceLabelOnTheDryRunAndTheCommit()
    {
        var sent = new List<PokestopEvent>();
        this.CreateAnswers(
            rules => { sent.AddRange(rules); return Created(Rule(0, PokestopEventTypes.Showcase)); },
            rules => { sent.AddRange(rules); return Created(Rule(735, PokestopEventTypes.Showcase)); });
        var model = Rule(0, PokestopEventTypes.Showcase, distance: 500);
        model.Ping = "<@&1>";
        model.OverrideLocationLabel = "home";

        var result = await this._sut.CreateAsync("u1", model);

        Assert.Equal(735, result.Uid);
        Assert.Equal(2, sent.Count);
        Assert.All(sent, rule =>
        {
            Assert.Equal("<@&1>", rule.Ping);
            Assert.Equal("home", rule.OverrideLocationLabel);
        });
        Assert.Equal("<@&1>", result.Ping);
        Assert.Equal("home", result.OverrideLocationLabel);
    }

    [Fact]
    public async Task CreateAsyncAllowsADifferentEventAlongsideAnExistingOne()
    {
        this.CreateAnswers(_ => Created(Rule(0, PokestopEventTypes.Kecleon)), _ => Created(Rule(734, PokestopEventTypes.Kecleon)));

        var result = await this._sut.CreateAsync("u1", Rule(0, PokestopEventTypes.Kecleon));

        Assert.Equal(734, result.Uid);
    }

    /// <summary>
    /// Nothing was written, so there is no uid to point a 201 at. The controller reads uid 0 and
    /// answers 200 rather than advertising a resource that 404s. See #459.
    /// </summary>
    [Fact]
    public async Task CreateAsyncReportsNoUidWhenPoracleFoundTheRuleAlreadyPresent()
    {
        this.CreateAnswers(_ => Unchanged(Rule(0, PokestopEventTypes.Showcase)), _ => throw new InvalidOperationException("no commit"));

        var result = await this._sut.CreateAsync("u1", Rule(0, PokestopEventTypes.Showcase));

        Assert.Equal(0, result.Uid);
        this.VerifyNoCommit();
    }

    /// <summary>
    /// Another write landed between the dry run and the commit, and the commit took a rule over after all.
    /// PoracleNG reports what that rule held before; it is put back and the create refused.
    /// </summary>
    [Fact]
    public async Task CreateAsyncPutsBackARuleTheCommitTookOverAndRefuses()
    {
        var previous = Rule(732, PokestopEventTypes.Showcase, distance: 500);
        this.CreateAnswers(
            _ => Created(Rule(0, PokestopEventTypes.Showcase)),
            _ => new PokestopEventWriteResult([], [Rule(732, PokestopEventTypes.Showcase, distance: 900)], [], [previous]));
        this._proxy.Setup(p => p.ReplaceAsync("u1", 732, It.IsAny<PokestopEvent>()))
            .ReturnsAsync(Updated(previous));

        var ex = await Assert.ThrowsAsync<TrackingConflictException>(
            () => this._sut.CreateAsync("u1", Rule(0, PokestopEventTypes.Showcase, distance: 900)));

        Assert.Equal(TakeoverMessage, ex.Message);
        Assert.Equal(732, ex.ExistingUid);
        this._proxy.Verify(p => p.ReplaceAsync("u1", 732, It.Is<PokestopEvent>(r => r.Distance == 500)), Times.Once);
    }

    [Fact]
    public async Task CreateAsyncIsRefusedWhenTheFeatureIsOff()
    {
        this._featureGate.Setup(g => g.EnsureEnabledAsync(DisableFeatureKeys.PokestopEvents))
            .ThrowsAsync(new FeatureDisabledException(DisableFeatureKeys.PokestopEvents));

        await Assert.ThrowsAsync<FeatureDisabledException>(
            () => this._sut.CreateAsync("u1", Rule(0, PokestopEventTypes.Showcase)));
    }

    // ── bulk create (the add dialog fans out one alarm per ticked box) ───────

    [Fact]
    public async Task BulkCreateAsyncPairsEachUidBackToItsEvent()
    {
        this.CreateAnswers(
            rules => Created([.. rules]),
            _ => Created(Rule(11, PokestopEventTypes.Kecleon), Rule(12, PokestopEventTypes.GoldStop)));

        var result = (await this._sut.BulkCreateAsync("u1", [
            Rule(0, PokestopEventTypes.GoldStop),
            Rule(0, PokestopEventTypes.Kecleon),
        ])).ToList();

        // Paired on the event, not on position: PoracleNG reorders its response.
        Assert.Equal(12, result[0].Uid);
        Assert.Equal(11, result[1].Uid);
    }

    [Fact]
    public async Task BulkCreateAsyncRefusesTheSameEventTwiceInOneRequest()
    {
        var ex = await Assert.ThrowsAsync<TrackingConflictException>(
            () => this._sut.BulkCreateAsync("u1", [
                Rule(0, PokestopEventTypes.Showcase),
                Rule(0, PokestopEventTypes.Showcase),
            ]));

        Assert.Equal("'showcase' is named twice in the same request.", ex.Message);
    }

    /// <summary>
    /// A mixed batch: the buckets carry no index back to the request, so each rule is dry-run alone. The new
    /// one is committed, the identical one and the takeover are left out with uid 0.
    /// </summary>
    [Fact]
    public async Task BulkCreateAsyncMixedBatchDryRunsEachRuleAndCommitsOnlyTheNewOne()
    {
        var committed = new List<List<PokestopEvent>>();
        this._proxy.Setup(p => p.CreateAsync("u1", It.IsAny<IEnumerable<PokestopEvent>>(), It.IsAny<bool>()))
            .ReturnsAsync((string _, IEnumerable<PokestopEvent> sent, bool dryRun) =>
            {
                List<PokestopEvent> rules = [.. sent];
                if (!dryRun)
                {
                    committed.Add(rules);
                    return Created(Rule(50, PokestopEventTypes.Kecleon));
                }

                if (rules.Count > 1)
                {
                    return new PokestopEventWriteResult(
                        [Rule(0, PokestopEventTypes.Kecleon)],
                        [Rule(7, PokestopEventTypes.GoldStop)],
                        [Rule(8, PokestopEventTypes.Showcase)]);
                }

                return rules[0].DisplayType switch
                {
                    PokestopEventTypes.GoldStop => Updated(Rule(7, PokestopEventTypes.GoldStop)),
                    PokestopEventTypes.Showcase => Unchanged(Rule(8, PokestopEventTypes.Showcase)),
                    _ => Created(Rule(0, PokestopEventTypes.Kecleon)),
                };
            });

        var result = (await this._sut.BulkCreateAsync("u1", [
            Rule(0, PokestopEventTypes.GoldStop),
            Rule(0, PokestopEventTypes.Kecleon),
            Rule(0, PokestopEventTypes.Showcase),
        ])).ToList();

        Assert.Equal([0, 50, 0], result.Select(r => r.Uid));
        Assert.Equal(PokestopEventTypes.Kecleon, Assert.Single(Assert.Single(committed)).DisplayType);
    }

    [Fact]
    public async Task BulkCreateAsyncAllExistingMakesNoPerRowRuns()
    {
        // The batch dry run creates nothing, so nothing would be written: there is no row to tell apart.
        this.CreateAnswers(
            _ => new PokestopEventWriteResult([], [Rule(7, PokestopEventTypes.GoldStop)], [Rule(8, PokestopEventTypes.Showcase)]),
            _ => throw new InvalidOperationException("nothing should be committed"));

        var result = (await this._sut.BulkCreateAsync("u1", [
            Rule(0, PokestopEventTypes.GoldStop),
            Rule(0, PokestopEventTypes.Showcase),
        ])).ToList();

        Assert.Equal([0, 0], result.Select(r => r.Uid));
        this._proxy.Verify(p => p.CreateAsync("u1", It.IsAny<IEnumerable<PokestopEvent>>(), true), Times.Once);
        this.VerifyNoCommit();
    }

    [Fact]
    public async Task BulkCreateAsyncAllNewDryRunsOnceAndCommitsOnce()
    {
        this.CreateAnswers(
            rules => Created([.. rules]),
            _ => Created(Rule(11, PokestopEventTypes.Kecleon), Rule(12, PokestopEventTypes.GoldStop)));

        await this._sut.BulkCreateAsync("u1", [Rule(0, PokestopEventTypes.GoldStop), Rule(0, PokestopEventTypes.Kecleon)]);

        this._proxy.Verify(p => p.CreateAsync("u1", It.IsAny<IEnumerable<PokestopEvent>>(), true), Times.Once);
        this._proxy.Verify(p => p.CreateAsync("u1", It.IsAny<IEnumerable<PokestopEvent>>(), false), Times.Once);
    }

    // ── update ───────────────────────────────────────────────────────────────

    /// <summary>
    /// An edit is a single v2 replace: PoracleNG answers 409 itself when the replacement would duplicate
    /// another rule (the proxy maps it to the takeover message). No list is read to second-guess it.
    /// </summary>
    [Fact]
    public async Task UpdateAsyncIsOneReplaceAndReadsNothing()
    {
        this._proxy.Setup(p => p.ReplaceAsync("u1", 733, It.IsAny<PokestopEvent>()))
            .ReturnsAsync(Updated(Rule(735, PokestopEventTypes.Showcase, 4242)));

        var result = await this._sut.UpdateAsync("u1", Rule(733, PokestopEventTypes.Showcase, distance: 4242));

        // On 5.2.x the replace is delete-then-insert upstream, so the surviving row can be under a new uid.
        Assert.Equal(735, result.Uid);
        this._proxy.Verify(p => p.GetByUserAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsyncPassesPoracleNgsConflictThrough()
    {
        this._proxy.Setup(p => p.ReplaceAsync("u1", 733, It.IsAny<PokestopEvent>()))
            .ThrowsAsync(new TrackingConflictException("incident", TakeoverMessage));

        var ex = await Assert.ThrowsAsync<TrackingConflictException>(
            () => this._sut.UpdateAsync("u1", Rule(733, PokestopEventTypes.Kecleon, distance: 100)));

        Assert.Equal(TakeoverMessage, ex.Message);
    }

    // ── bulk paths ───────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateDistanceByUidsAsyncRewritesOnlyTheSelectedRules()
    {
        this.Existing(
            Rule(1, PokestopEventTypes.Showcase, distance: 100),
            Rule(2, PokestopEventTypes.Kecleon, distance: 100));
        List<int>? sent = null;
        TrackingPatchSet? settings = null;
        this._proxy.Setup(p => p.PatchAsync("u1", It.IsAny<IReadOnlyList<int>>(), It.IsAny<TrackingPatchSet>()))
            .Callback<string, IReadOnlyList<int>, TrackingPatchSet>((_, uids, s) => (sent, settings) = ([.. uids], s))
            .ReturnsAsync(new TrackingPatchResult([2], [], []));

        var count = (await this._sut.UpdateDistanceByUidsAsync([2], "u1", 750)).Updated;

        Assert.Equal(1, count);
        Assert.Equal([2], sent);
        Assert.Equal(new TrackingPatchSet(Distance: 750), settings);
        this._proxy.Verify(
            p => p.CreateAsync(It.IsAny<string>(), It.IsAny<IEnumerable<PokestopEvent>>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAllByUserAsyncReportsWhatPoracleActuallyRemoved()
    {
        this.Existing(Rule(1, PokestopEventTypes.Showcase), Rule(2, PokestopEventTypes.Kecleon));
        this._proxy.Setup(p => p.BulkDeleteByUidsAsync("u1", It.IsAny<IEnumerable<int>>())).ReturnsAsync(2);

        Assert.Equal(2, await this._sut.DeleteAllByUserAsync("u1", 1));
    }

    [Fact]
    public async Task DeleteAllByUserAsyncSkipsTheCallWhenThereIsNothingToDelete()
    {
        Assert.Equal(0, await this._sut.DeleteAllByUserAsync("u1", 1));
        this._proxy.Verify(
            p => p.BulkDeleteByUidsAsync(It.IsAny<string>(), It.IsAny<IEnumerable<int>>()), Times.Never);
    }
}
