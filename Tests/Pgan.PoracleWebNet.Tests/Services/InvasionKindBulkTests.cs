using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// An invasion and a Pokestop event can share a uid. The invasion list names the kind of the cards it acts on, so a
/// bulk move or radius change of one never reaches the other.
/// </summary>
public class InvasionKindBulkTests
{
    private const string Discord = "100000000000000001";
    private const string Telegram = "200000001";

    private readonly Mock<IFeatureGate> _gate = new();
    private readonly Mock<IPoracleIncidentProxy> _incident = new();
    private readonly Mock<IPoracleTrackingProxy> _proxy = new();
    private readonly InvasionService _service;

    public InvasionKindBulkTests()
    {
        this._gate.Setup(g => g.EnsureEnabledAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        // The events page is off, so the invasion list holds both kinds.
        this._gate.Setup(g => g.IsEnabledAsync(It.IsAny<string>())).ReturnsAsync(false);
        this._proxy.Setup(p => p.GetByUserAsync("invasion", "u1")).ReturnsAsync(JsonSerializer.SerializeToElement(new object[]
        {
            new { uid = 1, id = "u1", grunt_type = "water", distance = 100 },
            new { uid = 1, id = "u1", grunt_type = "showcase", distance = 100 },
        }));
        this._proxy.Setup(p => p.PatchAsync("invasion", "u1", It.IsAny<IReadOnlyList<int>>(), It.IsAny<TrackingPatchSet>()))
            .ReturnsAsync((string _, string _, IReadOnlyList<int> uids, TrackingPatchSet _) => new TrackingPatchResult([.. uids], [], []));
        this._incident.Setup(p => p.PatchAsync("u1", It.IsAny<IReadOnlyList<int>>(), It.IsAny<TrackingPatchSet>()))
            .ReturnsAsync((string _, IReadOnlyList<int> uids, TrackingPatchSet _) => new TrackingPatchResult([.. uids], [], []));
        this._service = new InvasionService(this._proxy.Object, this._gate.Object, NullLogger<InvasionService>.Instance,
            Mock.Of<ITrackedUidRemapper>(), this._incident.Object);
    }

    [Fact]
    public async Task DistanceOfTheEventOnlyPatchesTheEventRoute()
    {
        var result = await this._service.UpdateDistanceByUidsAsync([1], "u1", 500, "event");

        Assert.Equal(1, result.Updated);
        this._incident.Verify(p => p.PatchAsync("u1", It.Is<IReadOnlyList<int>>(u => u.SequenceEqual(new[] { 1 })), It.IsAny<TrackingPatchSet>()), Times.Once);
        this._proxy.Verify(p => p.PatchAsync("invasion", It.IsAny<string>(), It.IsAny<IReadOnlyList<int>>(), It.IsAny<TrackingPatchSet>()), Times.Never);
    }

    [Fact]
    public async Task DistanceOfTheInvasionOnlyPatchesTheInvasionRoute()
    {
        var result = await this._service.UpdateDistanceByUidsAsync([1], "u1", 500, "invasion");

        Assert.Equal(1, result.Updated);
        this._proxy.Verify(p => p.PatchAsync("invasion", "u1", It.Is<IReadOnlyList<int>>(u => u.SequenceEqual(new[] { 1 })), It.IsAny<TrackingPatchSet>()), Times.Once);
        this._incident.Verify(p => p.PatchAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<int>>(), It.IsAny<TrackingPatchSet>()), Times.Never);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("bogus")]
    public async Task DistanceWithoutAKnownKindPatchesBothAsBefore(string? kind)
    {
        // Distinct uids: the uid-only call is for callers that do not know the kinds.
        this._proxy.Setup(p => p.GetByUserAsync("invasion", "u2")).ReturnsAsync(JsonSerializer.SerializeToElement(new object[]
        {
            new { uid = 1, id = "u2", grunt_type = "water", distance = 100 },
            new { uid = 2, id = "u2", grunt_type = "showcase", distance = 100 },
        }));
        this._proxy.Setup(p => p.PatchAsync("invasion", "u2", It.IsAny<IReadOnlyList<int>>(), It.IsAny<TrackingPatchSet>()))
            .ReturnsAsync((string _, string _, IReadOnlyList<int> uids, TrackingPatchSet _) => new TrackingPatchResult([.. uids], [], []));
        this._incident.Setup(p => p.PatchAsync("u2", It.IsAny<IReadOnlyList<int>>(), It.IsAny<TrackingPatchSet>()))
            .ReturnsAsync((string _, IReadOnlyList<int> uids, TrackingPatchSet _) => new TrackingPatchResult([.. uids], [], []));

        var result = await this._service.UpdateDistanceByUidsAsync([1, 2], "u2", 500, kind);

        Assert.Equal(2, result.Updated);
        this._proxy.Verify(p => p.PatchAsync("invasion", "u2", It.IsAny<IReadOnlyList<int>>(), It.IsAny<TrackingPatchSet>()), Times.Once);
        this._incident.Verify(p => p.PatchAsync("u2", It.IsAny<IReadOnlyList<int>>(), It.IsAny<TrackingPatchSet>()), Times.Once);
    }

    [Fact]
    public async Task RouterFindsTheOwnerOfTheKindOnTheOtherAccount()
    {
        var linked = new Mock<ILinkedAccount>();
        linked.Setup(l => l.ResolveAsync(Discord)).ReturnsAsync(Pair());
        var svc = new Mock<IInvasionService>();
        svc.Setup(s => s.GetByUserAsync(Discord, 1)).ReturnsAsync([new Invasion { Uid = 1, GruntType = "water" }]);
        svc.Setup(s => s.GetByUserAsync(Telegram, 1)).ReturnsAsync([new Invasion { Uid = 1, GruntType = "showcase" }]);
        svc.Setup(s => s.UpdateDistanceByUidsAsync(It.IsAny<List<int>>(), It.IsAny<string>(), 500, It.IsAny<string?>()))
            .ReturnsAsync(new DistanceUpdateResult(1, [], []));
        var router = new LinkedAlarmRouter(linked.Object, Controllers.ControllerTestBase.InertMover(linked.Object));

        await router.UpdateDistanceByUidsAsync(svc.Object, Discord, 1, [1], 500, "event");

        svc.Verify(s => s.UpdateDistanceByUidsAsync(It.Is<List<int>>(l => l.SequenceEqual(new[] { 1 })), Telegram, 500, "event"), Times.Once);
        svc.Verify(s => s.UpdateDistanceByUidsAsync(It.IsAny<List<int>>(), Discord, It.IsAny<int>(), It.IsAny<string?>()), Times.Never);
    }

    [Theory]
    [InlineData("event", "showcase")]
    [InlineData("invasion", "water")]
    public async Task BulkMoveOfOneKindMovesAndDeletesOnlyThatKind(string kind, string grunt)
    {
        var linked = new Mock<ILinkedAccount>();
        linked.Setup(l => l.ResolveAsync(It.IsAny<string>())).ReturnsAsync(Pair());
        var humans = new Mock<IHumanService>();
        humans.Setup(h => h.GetByIdAsync(It.IsAny<string>())).ReturnsAsync((string id) => new Human { Id = id, CurrentProfileNo = 1 });
        var human = new Mock<IPoracleHumanProxy>();
        human.Setup(p => p.GetPlacesAsync(It.IsAny<string>())).ReturnsAsync(new SavedPlaces());
        var repo = new Mock<IQuickPickAppliedStateRepository>();
        repo.Setup(r => r.GetByUserAndProfileAsync(It.IsAny<string>(), It.IsAny<int>())).ReturnsAsync([]);
        var mover = new AlarmMover(linked.Object, humans.Object, human.Object, Mock.Of<IPoracleTrackingProxy>(), Mock.Of<IUserAreaPolicy>(),
            new QuickPickUidTransfer(repo.Object, NullLogger<QuickPickUidTransfer>.Instance), NullLogger<AlarmMover>.Instance);

        var svc = new Mock<IInvasionService>();
        svc.Setup(s => s.GetByUserAsync(Discord, 1)).ReturnsAsync(
            [new Invasion { Uid = 5, GruntType = "water", Destination = "discord" }, new Invasion { Uid = 5, GruntType = "showcase", Destination = "discord" }]);
        svc.Setup(s => s.GetByUserAsync(Telegram, 1)).ReturnsAsync([]);
        Invasion? created = null;
        svc.Setup(s => s.CreateAsync(Telegram, It.IsAny<Invasion>()))
            .Callback<string, Invasion>((_, m) => created = m)
            .ReturnsAsync(new Invasion { Uid = 77 });
        svc.Setup(s => s.DeleteAsync(Discord, 5, kind)).ReturnsAsync(true);

        var results = await mover.MoveManyAsync(svc.Object, "invasion", Discord, 1, [5], LinkSide.Telegram, kind);

        Assert.Equal(MoveStatus.Moved, Assert.Single(results).Status);
        Assert.Equal(grunt, created!.GruntType);
        svc.Verify(s => s.DeleteAsync(Discord, 5, kind), Times.Once);
        svc.Verify(s => s.DeleteAsync(Discord, 5), Times.Never);
        svc.Verify(s => s.DeleteAsync(Discord, 5, kind == "event" ? "invasion" : "event"), Times.Never);
    }

    private static LinkedPair Pair() => new(
        Discord,
        new AccountLink(1, Discord, Telegram, LinkSide.Discord, null, null, DateTime.UtcNow),
        new PairSide(LinkSide.Discord, Discord, true, PauseReason.None),
        new PairSide(LinkSide.Telegram, Telegram, true, PauseReason.None));
}
