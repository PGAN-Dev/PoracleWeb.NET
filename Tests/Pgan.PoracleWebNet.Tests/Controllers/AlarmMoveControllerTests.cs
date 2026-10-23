using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Pgan.PoracleWebNet.Api.Controllers;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Controllers;

public class AlarmMoveControllerTests : ControllerTestBase
{
    private const string Discord = "100000000000000001";
    private const string Telegram = "200000001";

    private readonly Mock<IFeatureGate> _gate = new();
    private readonly Mock<ILinkedAccount> _linked = new();
    private readonly Mock<IHumanService> _humans = new();
    private readonly Mock<IPoracleHumanProxy> _proxy = new();
    private readonly Mock<IQuickPickAppliedStateRepository> _repo = new();
    private readonly Mock<IRaidService> _raids = new();
    private readonly AlarmMover _mover;

    public AlarmMoveControllerTests()
    {
        this._linked.Setup(l => l.ResolveAsync(Discord)).ReturnsAsync(Pair());
        this._humans.Setup(h => h.GetByIdAsync(It.IsAny<string>())).ReturnsAsync((string id) => new Human { Id = id, CurrentProfileNo = 1 });
        this._repo.Setup(r => r.GetByUserAndProfileAsync(It.IsAny<string>(), It.IsAny<int>())).ReturnsAsync([]);
        this._mover = new AlarmMover(
            this._linked.Object, this._humans.Object, this._proxy.Object, Mock.Of<IUserAreaPolicy>(),
            new QuickPickUidTransfer(this._repo.Object, NullLogger<QuickPickUidTransfer>.Instance),
            NullLogger<AlarmMover>.Instance);
    }

    private static LinkedPair Pair() => new(
        Discord,
        new AccountLink(1, Discord, Telegram, LinkSide.Discord, null, null, DateTime.UtcNow),
        new PairSide(LinkSide.Discord, Discord, true, PauseReason.None),
        new PairSide(LinkSide.Telegram, Telegram, true, PauseReason.None));

    private AlarmMoveController Controller()
    {
        var dispatcher = new AlarmMoveDispatcher(
            this._mover, Mock.Of<IMonsterService>(), this._raids.Object, Mock.Of<IEggService>(), Mock.Of<IQuestService>(),
            Mock.Of<IInvasionService>(), Mock.Of<ILureService>(), Mock.Of<INestService>(), Mock.Of<IGymService>(),
            Mock.Of<IMaxBattleService>(), Mock.Of<IFortChangeService>(), Mock.Of<IPokestopEventService>());
        var sut = new AlarmMoveController(this._gate.Object, this._linked.Object, dispatcher);
        SetupUser(sut, Discord);
        return sut;
    }

    [Fact]
    public async Task DisabledType_IsRefusedByTheGate()
    {
        this._gate.Setup(g => g.EnsureEnabledAsync(DisableFeatureKeys.Raids)).ThrowsAsync(new FeatureDisabledException(DisableFeatureKeys.Raids));

        await Assert.ThrowsAsync<FeatureDisabledException>(() =>
            this.Controller().Move(new AlarmMoveRequest { Type = "raid", Uids = [1], Destination = "telegram" }));
        this._raids.Verify(r => r.CreateAsync(It.IsAny<string>(), It.IsAny<Raid>()), Times.Never);
    }

    [Fact]
    public async Task Unlinked_Is400()
    {
        this._linked.Setup(l => l.ResolveAsync(Discord)).ReturnsAsync(LinkedPair.Single(Discord));

        var result = await this.Controller().Move(new AlarmMoveRequest { Type = "raid", Uids = [1], Destination = "telegram" });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task UnknownTypeOrDestination_Is400()
    {
        Assert.IsType<BadRequestObjectResult>(await this.Controller().Move(new AlarmMoveRequest { Type = "nope", Uids = [1], Destination = "telegram" }));
        Assert.IsType<BadRequestObjectResult>(await this.Controller().Move(new AlarmMoveRequest { Type = "raid", Uids = [1], Destination = "discord:user" }));
    }

    [Fact]
    public async Task MixedResultsPerUid()
    {
        this._raids.Setup(r => r.GetByUserAsync(Discord, 1)).ReturnsAsync([new Raid { Uid = 1, ProfileNo = 1 }, new Raid { Uid = 2, ProfileNo = 1 }]);
        this._raids.Setup(r => r.GetByUserAsync(Telegram, 1)).ReturnsAsync([]);
        this._raids.SetupSequence(r => r.CreateAsync(Telegram, It.IsAny<Raid>()))
            .ReturnsAsync(new Raid { Uid = 50 })
            .ThrowsAsync(new TrackingConflictException("raid", "in the way") { ExistingUid = 9 });
        this._raids.Setup(r => r.DeleteAsync(Discord, 1)).ReturnsAsync(true);

        var result = Assert.IsType<OkObjectResult>(await this.Controller().Move(
            new AlarmMoveRequest { Type = "raid", Uids = [1, 2, 3], Destination = "telegram" }));

        var results = (IReadOnlyList<MoveOutcome>)result.Value!.GetType().GetProperty("results")!.GetValue(result.Value)!;
        Assert.Equal([MoveStatus.Moved, MoveStatus.Conflict, MoveStatus.Refused], results.Select(r => r.Status));
        this._raids.Verify(r => r.DeleteAsync(Discord, 2), Times.Never);
    }

    private RaidController RaidController()
    {
        var sut = new RaidController(this._raids.Object, new LinkedAlarmRouter(this._linked.Object, this._mover));
        SetupUser(sut, Discord);
        return sut;
    }

    [Fact]
    public async Task Put_NamingTheOtherSide_Moves_AnswersTheAlarmOnTheTarget()
    {
        var sut = this.RaidController();
        var existing = new Raid { Uid = 1, ProfileNo = 0, Destination = "discord" };
        this._raids.Setup(r => r.GetByUidAsync(Discord, 1)).ReturnsAsync(existing);
        this._raids.Setup(r => r.CreateAsync(Telegram, It.IsAny<Raid>())).ReturnsAsync(new Raid { Uid = 50 });
        this._raids.Setup(r => r.DeleteAsync(Discord, 1)).ReturnsAsync(true);

        var result = await sut.Update(1, new RaidUpdate { Destination = "telegram" });

        var raid = Assert.IsType<Raid>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(50, raid.Uid);
        Assert.Equal("telegram", raid.Destination);
        Assert.Equal("moved", sut.Response.Headers["X-Move-Status"].ToString());
        this._raids.Verify(r => r.UpdateAsync(It.IsAny<string>(), It.IsAny<Raid>()), Times.Never);
    }

    [Fact]
    public async Task Put_MoveWhoseSourceCannotBeDeleted_Is200WithSourceRemainsHeader()
    {
        var sut = this.RaidController();
        this._raids.Setup(r => r.GetByUidAsync(Discord, 1)).ReturnsAsync(new Raid { Uid = 1 });
        this._raids.Setup(r => r.CreateAsync(Telegram, It.IsAny<Raid>())).ReturnsAsync(new Raid { Uid = 50 });
        this._raids.Setup(r => r.DeleteAsync(Discord, 1)).ThrowsAsync(new HttpRequestException("down"));

        var result = await sut.Update(1, new RaidUpdate { Destination = "telegram" });

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal("source_remains", sut.Response.Headers["X-Move-Status"].ToString());
    }

    [Fact]
    public async Task Put_NamingTheSameSide_IsAnOrdinaryEdit()
    {
        var existing = new Raid { Uid = 1, ProfileNo = 1 };
        this._raids.Setup(r => r.GetByUidAsync(Discord, 1)).ReturnsAsync(existing);
        this._raids.Setup(r => r.UpdateAsync(Discord, existing)).ReturnsAsync(existing);

        var result = await this.RaidController().Update(1, new RaidUpdate { Destination = "discord", Distance = 500 });

        Assert.IsType<OkObjectResult>(result);
        this._raids.Verify(r => r.UpdateAsync(Discord, existing), Times.Once);
        this._raids.Verify(r => r.CreateAsync(It.IsAny<string>(), It.IsAny<Raid>()), Times.Never);
    }

    [Fact]
    public async Task Put_MoveConflict_ReachesTheGlobalFilterAsTheTrackingConflict()
    {
        this._raids.Setup(r => r.GetByUidAsync(Discord, 1)).ReturnsAsync(new Raid { Uid = 1 });
        this._raids.Setup(r => r.CreateAsync(Telegram, It.IsAny<Raid>())).ThrowsAsync(new TrackingConflictException("raid", "x") { ExistingUid = 9 });

        var ex = await Assert.ThrowsAsync<TrackingConflictException>(() =>
            this.RaidController().Update(1, new RaidUpdate { Destination = "telegram" }));

        Assert.Equal(9, ex.ExistingUid);
    }

    [Fact]
    public async Task Put_MoveRefusedInPreflight_Is409WithError()
    {
        this._humans.Setup(h => h.GetByIdAsync(Telegram)).ReturnsAsync(new Human { Id = Telegram, CurrentProfileNo = 2 });
        this._raids.Setup(r => r.GetByUidAsync(Discord, 1)).ReturnsAsync(new Raid { Uid = 1 });

        var result = await this.RaidController().Update(1, new RaidUpdate { Destination = "telegram" });

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(409, obj.StatusCode);
        Assert.Contains("different profiles", obj.Value!.ToString());
        this._raids.Verify(r => r.CreateAsync(It.IsAny<string>(), It.IsAny<Raid>()), Times.Never);
    }

    [Fact]
    public async Task Put_MonsterWithInvertedIvWindowAndOtherDestination_Is400AndMovesNothing()
    {
        var monsters = new Mock<IMonsterService>();
        var sut = new MonsterController(monsters.Object, Mock.Of<IPokemonEditCapabilityService>(), new LinkedAlarmRouter(this._linked.Object, this._mover));
        SetupUser(sut, Discord);
        monsters.Setup(m => m.GetByUidAsync(Discord, 1)).ReturnsAsync(new Monster { Uid = 1 });

        var result = await sut.Update(1, new MonsterUpdate { MinIv = 90, MaxIv = 10, Destination = "telegram" });

        Assert.IsType<BadRequestObjectResult>(result);
        monsters.Verify(m => m.CreateAsync(It.IsAny<string>(), It.IsAny<Monster>()), Times.Never);
    }

    [Fact]
    public async Task Put_MonsterWithValidWindowAndOtherDestination_Moves()
    {
        var monsters = new Mock<IMonsterService>();
        var sut = new MonsterController(monsters.Object, Mock.Of<IPokemonEditCapabilityService>(), new LinkedAlarmRouter(this._linked.Object, this._mover));
        SetupUser(sut, Discord);
        monsters.Setup(m => m.GetByUidAsync(Discord, 1)).ReturnsAsync(new Monster { Uid = 1 });
        monsters.Setup(m => m.CreateAsync(Telegram, It.IsAny<Monster>())).ReturnsAsync(new Monster { Uid = 50 });
        monsters.Setup(m => m.DeleteAsync(Discord, 1)).ReturnsAsync(true);

        var result = await sut.Update(1, new MonsterUpdate { MinIv = 10, MaxIv = 90, Destination = "telegram" });

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task Post_TooManyUids_FailsModelValidation()
    {
        var request = new AlarmMoveRequest { Type = "raid", Uids = [.. Enumerable.Range(1, 501)], Destination = "telegram" };
        var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();

        Assert.False(System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            request, new System.ComponentModel.DataAnnotations.ValidationContext(request), results, true));
        await Task.CompletedTask;
    }
}
