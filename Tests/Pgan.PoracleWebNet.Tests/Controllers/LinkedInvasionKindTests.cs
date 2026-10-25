using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Pgan.PoracleWebNet.Api.Controllers;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Controllers;

/// <summary>
/// Invasion and Pokestop event rules keep separate uid sequences (#1058, #1062), and a linked pair keeps its
/// alarms on two humans. These hold the two together: the stated kind is looked for on both humans, a kind change
/// stays on the owner, a move never mixes the two kinds, and an Areas save that dropped dead names writes the
/// cleaned list to both humans.
/// </summary>
public class LinkedInvasionKindTests : ControllerTestBase
{
    private const string Discord = "100000000000000001";
    private const string Telegram = "200000001";

    private readonly Mock<ILinkedAccount> _linked = new();
    private readonly Mock<IHumanService> _humans = new();
    private readonly Mock<IPoracleTrackingProxy> _tracking = new();
    private readonly Mock<IInvasionService> _invasions = new();
    private readonly AlarmMover _mover;

    public LinkedInvasionKindTests()
    {
        this._linked.Setup(l => l.ResolveAsync(It.IsAny<string>())).ReturnsAsync(Pair());
        this._humans.Setup(h => h.GetByIdAsync(It.IsAny<string>())).ReturnsAsync((string id) => new Human { Id = id, CurrentProfileNo = 1 });
        var repo = new Mock<IQuickPickAppliedStateRepository>();
        repo.Setup(r => r.GetByUserAndProfileAsync(It.IsAny<string>(), It.IsAny<int>())).ReturnsAsync([]);
        this._mover = new AlarmMover(
            this._linked.Object, this._humans.Object, Mock.Of<IPoracleHumanProxy>(), this._tracking.Object, Mock.Of<IUserAreaPolicy>(),
            new QuickPickUidTransfer(repo.Object, NullLogger<QuickPickUidTransfer>.Instance),
            NullLogger<AlarmMover>.Instance);
    }

    private static LinkedPair Pair() => new(
        Discord,
        new AccountLink(1, Discord, Telegram, LinkSide.Discord, null, null, DateTime.UtcNow),
        new PairSide(LinkSide.Discord, Discord, true, PauseReason.None),
        new PairSide(LinkSide.Telegram, Telegram, true, PauseReason.None));

    private InvasionController Controller(bool impersonating = false)
    {
        var sut = new InvasionController(this._invasions.Object, new LinkedAlarmRouter(this._linked.Object, this._mover));
        SetupUser(sut, Discord, impersonatedBy: impersonating ? "999" : null);
        return sut;
    }

    [Fact]
    public async Task TheStatedKindIsFoundOnTheHumanThatOwnsIt()
    {
        // Discord holds an invasion numbered 7; the event numbered 7 the person opened is on Telegram.
        var invasionOnDiscord = new Invasion { Uid = 7, GruntType = "water" };
        var eventOnTelegram = new Invasion { Uid = 7, GruntType = "kecleon" };
        this._invasions.Setup(s => s.GetByUidAsync(Discord, 7, "event")).ReturnsAsync((Invasion?)null);
        this._invasions.Setup(s => s.GetByUidAsync(Telegram, 7, "event")).ReturnsAsync(eventOnTelegram);
        this._invasions.Setup(s => s.GetByUidAsync(Discord, 7)).ReturnsAsync(invasionOnDiscord);
        this._invasions.Setup(s => s.UpdateAsync(Telegram, eventOnTelegram, "event")).ReturnsAsync(eventOnTelegram);

        var result = await this.Controller().Update(7, new InvasionUpdate { Distance = 500 }, "event");

        Assert.IsType<OkObjectResult>(result);
        this._invasions.Verify(s => s.UpdateAsync(Telegram, eventOnTelegram, "event"), Times.Once);
        this._invasions.Verify(s => s.UpdateAsync(Discord, It.IsAny<Invasion>(), It.IsAny<string?>()), Times.Never);
        Assert.Equal("telegram", eventOnTelegram.Destination);
    }

    [Fact]
    public async Task AKindChangeOnALinkedAlarmStaysOnItsOwner()
    {
        var invasionOnTelegram = new Invasion { Uid = 7, GruntType = "water" };
        this._invasions.Setup(s => s.GetByUidAsync(Telegram, 7, "invasion")).ReturnsAsync(invasionOnTelegram);
        this._invasions.Setup(s => s.UpdateAsync(Telegram, invasionOnTelegram, "invasion")).ReturnsAsync(invasionOnTelegram);

        var result = await this.Controller().Update(7, new InvasionUpdate { GruntType = "kecleon" }, "invasion");

        Assert.IsType<OkObjectResult>(result);
        this._invasions.Verify(s => s.UpdateAsync(Telegram, It.Is<Invasion>(i => i.GruntType == "kecleon"), "invasion"), Times.Once);
        this._invasions.Verify(s => s.CreateAsync(It.IsAny<string>(), It.IsAny<Invasion>()), Times.Never);
    }

    [Theory]
    [InlineData("invasion", "water", "kecleon")]
    [InlineData("event", "kecleon", "water")]
    [InlineData(null, "water", "kecleon")]
    public async Task AMoveCombinedWithAKindChangeIsRefusedBeforeAnythingIsWritten(string? kind, string stored, string edited)
    {
        var existing = new Invasion { Uid = 7, GruntType = stored };
        this._invasions.Setup(s => s.GetByUidAsync(Discord, 7)).ReturnsAsync(existing);
        this._invasions.Setup(s => s.GetByUidAsync(Discord, 7, It.IsAny<string?>())).ReturnsAsync(existing);

        var result = await this.Controller().Update(7, new InvasionUpdate { GruntType = edited, Destination = "telegram" }, kind);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("not both at once", bad.Value!.ToString());
        this._invasions.Verify(s => s.CreateAsync(It.IsAny<string>(), It.IsAny<Invasion>()), Times.Never);
        this._invasions.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        this._invasions.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task AMoveWithoutAKindChangeStillMoves()
    {
        var existing = new Invasion { Uid = 7, GruntType = "water" };
        this._invasions.Setup(s => s.GetByUidAsync(Discord, 7, "invasion")).ReturnsAsync(existing);
        this._invasions.Setup(s => s.CreateAsync(Telegram, It.IsAny<Invasion>())).ReturnsAsync(new Invasion { Uid = 50, GruntType = "water" });

        var result = await this.Controller().Update(7, new InvasionUpdate { Distance = 100, Destination = "telegram" }, "invasion");

        Assert.Equal(50, Assert.IsType<Invasion>(Assert.IsType<OkObjectResult>(result).Value).Uid);
        this._invasions.Verify(s => s.DeleteAsync(Discord, 7, "invasion"), Times.Once);
    }

    [Fact]
    public async Task AMovedEventIsDeletedAsAnEventSoAnInvasionSharingItsUidSurvives()
    {
        this._invasions.Setup(s => s.CreateAsync(Telegram, It.IsAny<Invasion>())).ReturnsAsync(new Invasion { Uid = 50, GruntType = "kecleon" });

        var result = await this._mover.MoveAsync<Invasion>(this._invasions.Object, "invasion", Discord,
            new Invasion { Uid = 7, GruntType = "kecleon" }, Discord, LinkSide.Telegram);

        Assert.Equal(MoveStatus.Moved, result.Outcome.Status);
        this._invasions.Verify(s => s.DeleteAsync(Discord, 7, "event"), Times.Once);
        this._invasions.Verify(s => s.DeleteAsync(Discord, 7), Times.Never);
    }

    [Fact]
    public async Task AMovedInvasionCarriesItsOwnStoredFieldsNotTheEventsSharingItsUid()
    {
        this._tracking.Setup(t => t.GetByUserAsync("invasion", Discord)).ReturnsAsync(JsonDocument.Parse("""
            [
              {"uid":7,"grunt_type":"kecleon","zz_event_only":"event"},
              {"uid":7,"grunt_type":"water","zz_invasion_only":"invasion"}
            ]
            """).RootElement.Clone());
        Invasion? sent = null;
        this._invasions.Setup(s => s.CreateAsync(Telegram, It.IsAny<Invasion>()))
            .Callback<string, Invasion>((_, m) => sent = m)
            .ReturnsAsync(new Invasion { Uid = 50 });

        await this._mover.MoveAsync<Invasion>(this._invasions.Object, "invasion", Discord,
            new Invasion { Uid = 7, GruntType = "water" }, Discord, LinkSide.Telegram);

        Assert.NotNull(sent);
        Assert.True(CarriedFields.TryGet(sent!, out var carried));
        Assert.Equal("invasion", carried.GetProperty("zz_invasion_only").GetString());
        Assert.False(carried.TryGetProperty("zz_event_only", out _));
    }

    [Fact]
    public async Task ABulkMoveNamingASharedUidMovesThePlainInvasion()
    {
        this._invasions.Setup(s => s.GetByUserAsync(Discord, 1)).ReturnsAsync(
            [new Invasion { Uid = 7, GruntType = "kecleon" }, new Invasion { Uid = 7, GruntType = "water" }]);
        this._invasions.Setup(s => s.GetByUserAsync(Telegram, 1)).ReturnsAsync([]);
        Invasion? sent = null;
        this._invasions.Setup(s => s.CreateAsync(Telegram, It.IsAny<Invasion>()))
            .Callback<string, Invasion>((_, m) => sent = m)
            .ReturnsAsync(new Invasion { Uid = 50 });

        var results = await this._mover.MoveManyAsync<Invasion>(this._invasions.Object, "invasion", Discord, 1, [7], LinkSide.Telegram);

        Assert.Equal(MoveStatus.Moved, Assert.Single(results).Status);
        Assert.Equal("water", sent!.GruntType);
        this._invasions.Verify(s => s.DeleteAsync(Discord, 7, "invasion"), Times.Once);
    }

    [Fact]
    public async Task AnAreasSaveThatDroppedDeadNamesWritesTheCleanedListToBothHumans()
    {
        // The page left the dead name out of what it sends (#1060); the paired write must send that same list to both.
        var writer = new Mock<IUserAreaWriter>();
        writer.Setup(w => w.SaveSelectionAsync(It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>()))
            .ReturnsAsync((string _, IReadOnlyCollection<string> names) => new AreaSaveResult(names.ToList(), []));
        var sut = new AreaController(
            Mock.Of<IPoracleHumanProxy>(), Mock.Of<IPoracleApiProxy>(), Mock.Of<IUserGeofenceService>(), writer.Object,
            new PairedHumanWriter(this._linked.Object, NullLogger<PairedHumanWriter>.Instance), Mock.Of<ILogger<AreaController>>());
        SetupUser(sut, Discord);

        await sut.UpdateAreas(new AreaController.UpdateAreasRequest { Areas = ["Downtown", "Old Town"] });

        foreach (var human in new[] { Discord, Telegram })
        {
            writer.Verify(w => w.SaveSelectionAsync(human, It.Is<IReadOnlyCollection<string>>(n => n.SequenceEqual(new[] { "downtown", "old town" }))), Times.Once);
        }
    }
}
