using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

public class LinkedAlarmRouterTests
{
    private const string Discord = "100000000000000001";
    private const string Telegram = "200000001";

    private readonly Mock<ILinkedAccount> _linked = new();
    private readonly Mock<IMonsterService> _svc = new();
    private readonly LinkedAlarmRouter _sut;

    public LinkedAlarmRouterTests() => this._sut = new LinkedAlarmRouter(this._linked.Object);

    private static AccountLink Link(LinkSide def = LinkSide.Discord) => new(1, Discord, Telegram, def, null, null, DateTime.UtcNow);

    private void Unlinked() => this._linked.Setup(l => l.ResolveAsync(It.IsAny<string>())).ReturnsAsync((string id) => LinkedPair.Single(id));

    private void Linked(string caller = Discord, bool telegramExists = true, PauseReason telegramPause = PauseReason.None, LinkSide def = LinkSide.Discord) =>
        this._linked.Setup(l => l.ResolveAsync(caller)).ReturnsAsync(new LinkedPair(
            caller,
            Link(def),
            new PairSide(LinkSide.Discord, Discord, true, PauseReason.None),
            new PairSide(LinkSide.Telegram, Telegram, telegramExists, telegramExists ? telegramPause : PauseReason.NotRegistered)));

    [Fact]
    public async Task List_Unlinked_SingleCallNoDestination()
    {
        this.Unlinked();
        this._svc.Setup(s => s.GetByUserAsync(Discord, 1)).ReturnsAsync([new Monster { Uid = 1 }]);

        var list = await this._sut.ListAsync(this._svc.Object, Discord, 1);

        Assert.Single(list);
        Assert.Null(list[0].Destination);
        this._svc.Verify(s => s.GetByUserAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Once);
    }

    [Fact]
    public async Task List_Linked_ConcatenatesAndStamps()
    {
        this.Linked();
        this._svc.Setup(s => s.GetByUserAsync(Discord, 1)).ReturnsAsync([new Monster { Uid = 1 }]);
        this._svc.Setup(s => s.GetByUserAsync(Telegram, 1)).ReturnsAsync([new Monster { Uid = 2 }]);

        var list = await this._sut.ListAsync(this._svc.Object, Discord, 1);

        Assert.Equal(["discord", "telegram"], list.Select(m => m.Destination));
        Assert.Equal([1, 2], list.Select(m => m.Uid));
    }

    [Fact]
    public async Task List_Linked_SkipsASideThatDoesNotExist()
    {
        this.Linked(telegramExists: false);
        this._svc.Setup(s => s.GetByUserAsync(Discord, 1)).ReturnsAsync([new Monster { Uid = 1 }]);

        var list = await this._sut.ListAsync(this._svc.Object, Discord, 1);

        Assert.Single(list);
        this._svc.Verify(s => s.GetByUserAsync(Telegram, It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task Find_UidOnOtherSide_ReturnsOther()
    {
        this.Linked();
        this._svc.Setup(s => s.GetByUidAsync(Telegram, 9)).ReturnsAsync(new Monster { Uid = 9 });

        var found = await this._sut.FindAsync(this._svc.Object, Discord, 9);

        Assert.NotNull(found);
        Assert.Equal(Telegram, found.Value.OwnerId);
        Assert.Equal("telegram", found.Value.Model.Destination);
    }

    [Fact]
    public async Task Find_UidOnNeither_ReturnsNull()
    {
        this.Linked();

        Assert.Null(await this._sut.FindAsync(this._svc.Object, Discord, 9));
    }

    [Fact]
    public async Task Find_Unlinked_AsksOnlyTheCaller()
    {
        this.Unlinked();
        this._svc.Setup(s => s.GetByUidAsync(Discord, 9)).ReturnsAsync(new Monster { Uid = 9 });

        var found = await this._sut.FindAsync(this._svc.Object, Discord, 9);

        Assert.Equal(Discord, found!.Value.OwnerId);
        Assert.Null(found.Value.Model.Destination);
        this._svc.Verify(s => s.GetByUidAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Once);
    }

    [Fact]
    public async Task UpdateDistanceByUids_GroupsByOwner()
    {
        this.Linked();
        this._svc.Setup(s => s.GetByUidAsync(Discord, 1)).ReturnsAsync(new Monster { Uid = 1 });
        this._svc.Setup(s => s.GetByUidAsync(Telegram, 2)).ReturnsAsync(new Monster { Uid = 2 });
        this._svc.Setup(s => s.UpdateDistanceByUidsAsync(It.IsAny<List<int>>(), Discord, 500)).ReturnsAsync(new DistanceUpdateResult(1, [], [], []));
        this._svc.Setup(s => s.UpdateDistanceByUidsAsync(It.IsAny<List<int>>(), Telegram, 500)).ReturnsAsync(new DistanceUpdateResult(1, [2], [], [7]));

        var result = await this._sut.UpdateDistanceByUidsAsync(this._svc.Object, Discord, [1, 2], 500);

        Assert.Equal(2, result.Updated);
        Assert.Equal([2], result.SkippedAreaScoped);
        Assert.Equal([7], result.SkippedConflict);
        this._svc.Verify(s => s.UpdateDistanceByUidsAsync(It.Is<List<int>>(l => l.SequenceEqual(new[] { 1 })), Discord, 500), Times.Once);
        this._svc.Verify(s => s.UpdateDistanceByUidsAsync(It.Is<List<int>>(l => l.SequenceEqual(new[] { 2 })), Telegram, 500), Times.Once);
    }

    [Fact]
    public async Task UpdateDistanceByUids_Unlinked_PassesTheListStraightThrough()
    {
        this.Unlinked();
        var uids = new List<int> { 1, 2 };
        this._svc.Setup(s => s.UpdateDistanceByUidsAsync(uids, Discord, 5)).ReturnsAsync(new DistanceUpdateResult(2, [], []));

        var result = await this._sut.UpdateDistanceByUidsAsync(this._svc.Object, Discord, uids, 5);

        Assert.Equal(2, result.Updated);
        this._svc.Verify(s => s.GetByUidAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAll_Linked_HitsBoth()
    {
        this.Linked();
        this._svc.Setup(s => s.DeleteAllByUserAsync(Discord, 1)).ReturnsAsync(3);
        this._svc.Setup(s => s.DeleteAllByUserAsync(Telegram, 1)).ReturnsAsync(4);

        Assert.Equal(7, await this._sut.DeleteAllAsync(this._svc.Object, Discord, 1));
    }

    [Fact]
    public async Task Count_Linked_SumsBoth()
    {
        this.Linked();
        this._svc.Setup(s => s.CountByUserAsync(Discord, 1)).ReturnsAsync(3);
        this._svc.Setup(s => s.CountByUserAsync(Telegram, 1)).ReturnsAsync(4);

        Assert.Equal(7, await this._sut.CountAsync(this._svc.Object, Discord, 1));
    }

    [Fact]
    public async Task UpdateDistanceAll_Linked_MergesBoth()
    {
        this.Linked();
        this._svc.Setup(s => s.UpdateDistanceByUserAsync(Discord, 1, 5)).ReturnsAsync(new DistanceUpdateResult(1, [], []));
        this._svc.Setup(s => s.UpdateDistanceByUserAsync(Telegram, 1, 5)).ReturnsAsync(new DistanceUpdateResult(2, [], [8]));

        var result = await this._sut.UpdateDistanceAllAsync(this._svc.Object, Discord, 1, 5);

        Assert.Equal(3, result.Updated);
        Assert.Equal([8], result.SkippedPlaceScoped);
    }

    [Fact]
    public async Task CreateTarget_Null_UsesDefault()
    {
        this.Linked(def: LinkSide.Telegram);

        Assert.Equal(Telegram, await this._sut.CreateTargetAsync(Discord, null));
    }

    [Fact]
    public async Task CreateTarget_NamedSide_WinsOverDefault()
    {
        this.Linked(def: LinkSide.Telegram);

        Assert.Equal(Discord, await this._sut.CreateTargetAsync(Discord, "discord"));
    }

    [Fact]
    public async Task CreateTarget_UnregisteredSide_Throws409()
    {
        this.Linked(telegramExists: false);

        var ex = await Assert.ThrowsAsync<AlarmDestinationException>(() => this._sut.CreateTargetAsync(Discord, "telegram"));

        Assert.Equal(409, ex.StatusCode);
        Assert.Equal("Telegram isn't set up yet.", ex.Message);
    }

    [Fact]
    public async Task CreateTarget_UnknownDestination_Throws400()
    {
        this.Linked();

        var ex = await Assert.ThrowsAsync<AlarmDestinationException>(() => this._sut.CreateTargetAsync(Discord, "carrier-pigeon"));

        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task CreateTarget_Unlinked_IgnoresDestination()
    {
        this.Unlinked();

        Assert.Equal(Discord, await this._sut.CreateTargetAsync(Discord, "telegram"));
    }

    [Fact]
    public async Task IsPaused_ReportsAPausedSideOfTheResolvedPair()
    {
        this.Linked(telegramPause: PauseReason.Stopped);
        await this._sut.CreateTargetAsync(Discord, "telegram");

        Assert.True(this._sut.IsPaused(Telegram));
        Assert.False(this._sut.IsPaused(Discord));
    }

    [Fact]
    public async Task StampCreated_FlagsOnlyAPausedTarget()
    {
        this.Linked(telegramPause: PauseReason.Stopped);
        await this._sut.CreateTargetAsync(Discord, "telegram");
        var paused = new Monster();
        var live = new Monster();

        this._sut.StampCreated(paused, Telegram);
        this._sut.StampCreated(live, Discord);

        Assert.True(paused.PausedDestination);
        Assert.Equal("telegram", paused.Destination);
        Assert.Null(live.PausedDestination);
    }

    [Fact]
    public async Task StampCreated_Unlinked_LeavesTheModelAlone()
    {
        this.Unlinked();
        await this._sut.CreateTargetAsync(Discord, null);
        var model = new Monster();

        this._sut.StampCreated(model, Discord);

        Assert.Null(model.Destination);
        Assert.Null(model.PausedDestination);
    }
}
