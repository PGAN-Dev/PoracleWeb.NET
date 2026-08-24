using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// Pokestop-event alarms. The behaviours asserted here were verified against a live PoracleNG 5.2.1,
/// not read off its source.
/// </summary>
public class PokestopEventServiceTests
{
    private readonly Mock<IPoracleIncidentProxy> _proxy = new();
    private readonly Mock<IFeatureGate> _featureGate = new();
    private readonly PokestopEventService _sut;

    public PokestopEventServiceTests()
    {
        this._featureGate.Setup(g => g.EnsureEnabledAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        this._proxy.Setup(p => p.GetByUserAsync(It.IsAny<string>())).ReturnsAsync([]);
        this._sut = new PokestopEventService(this._proxy.Object, this._featureGate.Object);
    }

    private static PokestopEvent Rule(int uid, int displayType, int distance = 100) => new()
    {
        Uid = uid,
        DisplayType = displayType,
        Distance = distance,
    };

    private void Existing(params PokestopEvent[] rules) =>
        this._proxy.Setup(p => p.GetByUserAsync("u1")).ReturnsAsync(rules);

    private void CreateReturns(PokestopEventWriteResult result) =>
        this._proxy.Setup(p => p.CreateAsync("u1", It.IsAny<IEnumerable<PokestopEvent>>())).ReturnsAsync(result);

    // ── create ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsyncAssignsTheUidPoracleNamed()
    {
        this.CreateReturns(new PokestopEventWriteResult([Rule(732, PokestopEventTypes.Showcase)], [], []));

        var result = await this._sut.CreateAsync("u1", Rule(0, PokestopEventTypes.Showcase));

        Assert.Equal(732, result.Uid);
    }

    /// <summary>
    /// Verified on 5.2.1: a create naming an event the user already tracks does not add a rule. It
    /// rewrites the existing one and re-keys it (732 became 733), so PoracleWeb would have answered
    /// 201 with a new uid for an alarm the user already had, at a radius they had just lost.
    /// </summary>
    [Fact]
    public async Task CreateAsyncRefusesAnEventTheUserAlreadyTracks()
    {
        this.Existing(Rule(732, PokestopEventTypes.Showcase, distance: 500));

        var ex = await Assert.ThrowsAsync<TrackingConflictException>(
            () => this._sut.CreateAsync("u1", Rule(0, PokestopEventTypes.Showcase, distance: 900)));

        Assert.Contains("showcase", ex.Message, StringComparison.OrdinalIgnoreCase);
        this._proxy.Verify(
            p => p.CreateAsync(It.IsAny<string>(), It.IsAny<IEnumerable<PokestopEvent>>()), Times.Never);
    }

    /// <summary>The legitimate-case half: a different event alongside an existing one is fine.</summary>
    [Fact]
    public async Task CreateAsyncAllowsADifferentEventAlongsideAnExistingOne()
    {
        this.Existing(Rule(732, PokestopEventTypes.Showcase));
        this.CreateReturns(new PokestopEventWriteResult([Rule(734, PokestopEventTypes.Kecleon)], [], []));

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
        this.CreateReturns(new PokestopEventWriteResult([], [], [Rule(0, PokestopEventTypes.Showcase)]));

        var result = await this._sut.CreateAsync("u1", Rule(0, PokestopEventTypes.Showcase));

        Assert.Equal(0, result.Uid);
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
        this.CreateReturns(new PokestopEventWriteResult(
            [Rule(11, PokestopEventTypes.Kecleon), Rule(12, PokestopEventTypes.GoldStop)], [], []));

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
        await Assert.ThrowsAsync<TrackingConflictException>(
            () => this._sut.BulkCreateAsync("u1", [
                Rule(0, PokestopEventTypes.Showcase),
                Rule(0, PokestopEventTypes.Showcase),
            ]));
    }

    // ── update ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Verified on 5.2.1: the v2 replace refuses only an exact duplicate. Moving a Showcase rule onto
    /// Kecleon while a Kecleon rule existed at a different radius left TWO Kecleon rules — a state no
    /// other path can produce and the list has no way to describe. Refuse before writing.
    /// </summary>
    [Fact]
    public async Task UpdateAsyncRefusesMovingARuleOntoAnEventAnotherRuleHolds()
    {
        this.Existing(
            Rule(733, PokestopEventTypes.Showcase, distance: 900),
            Rule(734, PokestopEventTypes.Kecleon, distance: 100));

        await Assert.ThrowsAsync<TrackingConflictException>(
            () => this._sut.UpdateAsync("u1", Rule(733, PokestopEventTypes.Kecleon, distance: 4242)));

        this._proxy.Verify(
            p => p.ReplaceAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<PokestopEvent>()), Times.Never);
    }

    /// <summary>
    /// The legitimate-case half. Keeping the event and changing only the radius is the ordinary edit,
    /// and a refusal keyed on the event alone would have made every alarm permanently uneditable —
    /// the shape of #553.
    /// </summary>
    [Fact]
    public async Task UpdateAsyncAllowsChangingTheRadiusOfTheEventTheRuleAlreadyHolds()
    {
        this.Existing(
            Rule(733, PokestopEventTypes.Showcase, distance: 900),
            Rule(734, PokestopEventTypes.Kecleon, distance: 100));
        this._proxy.Setup(p => p.ReplaceAsync("u1", 733, It.IsAny<PokestopEvent>()))
            .ReturnsAsync(new PokestopEventWriteResult([], [Rule(735, PokestopEventTypes.Showcase, 4242)], []));

        var result = await this._sut.UpdateAsync("u1", Rule(733, PokestopEventTypes.Showcase, distance: 4242));

        // The replace is delete-then-insert upstream, so the surviving row is under a new uid.
        Assert.Equal(735, result.Uid);
    }

    // ── bulk paths ───────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateDistanceByUidsAsyncRewritesOnlyTheSelectedRules()
    {
        this.Existing(
            Rule(1, PokestopEventTypes.Showcase, distance: 100),
            Rule(2, PokestopEventTypes.Kecleon, distance: 100));
        List<PokestopEvent>? sent = null;
        this._proxy.Setup(p => p.CreateAsync("u1", It.IsAny<IEnumerable<PokestopEvent>>()))
            .Callback<string, IEnumerable<PokestopEvent>>((_, rules) => sent = [.. rules])
            .ReturnsAsync(new PokestopEventWriteResult([], [], []));

        var count = await this._sut.UpdateDistanceByUidsAsync([2], "u1", 750);

        Assert.Equal(1, count);
        Assert.Equal(PokestopEventTypes.Kecleon, Assert.Single(sent!).DisplayType);
        Assert.Equal(750, sent![0].Distance);
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
