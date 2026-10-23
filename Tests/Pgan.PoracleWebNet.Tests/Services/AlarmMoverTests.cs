using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

public class AlarmMoverTests
{
    private const string Discord = "100000000000000001";
    private const string Telegram = "200000001";

    private readonly Mock<ILinkedAccount> _linked = new();
    private readonly Mock<IHumanService> _humans = new();
    private readonly Mock<IPoracleHumanProxy> _proxy = new();
    private readonly Mock<IUserAreaPolicy> _policy = new();
    private readonly Mock<IQuickPickAppliedStateRepository> _repo = new();
    private readonly Mock<IMonsterService> _svc = new(MockBehavior.Strict);
    private readonly AlarmMover _sut;

    public AlarmMoverTests()
    {
        this._linked.Setup(l => l.ResolveAsync(It.IsAny<string>())).ReturnsAsync(Pair());
        this._humans.Setup(h => h.GetByIdAsync(Telegram)).ReturnsAsync(new Human { Id = Telegram, CurrentProfileNo = 1 });
        this._humans.Setup(h => h.GetByIdAsync(Discord)).ReturnsAsync(new Human { Id = Discord, CurrentProfileNo = 1 });
        this._proxy.Setup(p => p.GetPlacesAsync(It.IsAny<string>())).ReturnsAsync(new SavedPlaces
        {
            Named = [new SavedPlace { Label = "Home" }]
        });
        this._repo.Setup(r => r.GetByUserAndProfileAsync(It.IsAny<string>(), It.IsAny<int>())).ReturnsAsync([]);
        this._sut = new AlarmMover(
            this._linked.Object,
            this._humans.Object,
            this._proxy.Object,
            this._policy.Object,
            new QuickPickUidTransfer(this._repo.Object, NullLogger<QuickPickUidTransfer>.Instance),
            NullLogger<AlarmMover>.Instance);
    }

    private static LinkedPair Pair() => new(
        Discord,
        new AccountLink(1, Discord, Telegram, LinkSide.Discord, null, null, DateTime.UtcNow),
        new PairSide(LinkSide.Discord, Discord, true, PauseReason.None),
        new PairSide(LinkSide.Telegram, Telegram, true, PauseReason.None));

    private static Monster Model(int uid = 5) => new() { Uid = uid, ProfileNo = 1, Destination = "discord" };

    private Task<MoveOutcome> Move(Monster m) =>
        this._sut.MoveAsync<Monster>(this._svc.Object, "pokemon", Discord, m, Discord, LinkSide.Telegram);

    [Fact]
    public async Task Move_HappyPath_CreatesThenDeletes()
    {
        var seq = new MockSequence();
        this._svc.InSequence(seq).Setup(s => s.CreateAsync(Telegram, It.IsAny<Monster>())).ReturnsAsync(new Monster { Uid = 77 });
        this._svc.InSequence(seq).Setup(s => s.DeleteAsync(Discord, 5)).ReturnsAsync(true);

        var outcome = await this.Move(Model());

        Assert.Equal(new MoveOutcome(5, MoveStatus.Moved, 77), outcome);
    }

    [Fact]
    public async Task Move_RefusesWhenTargetCurrentProfileDiffers()
    {
        this._humans.Setup(h => h.GetByIdAsync(Telegram)).ReturnsAsync(new Human { Id = Telegram, CurrentProfileNo = 2 });

        var outcome = await this.Move(Model());

        Assert.Equal(MoveStatus.Refused, outcome.Status);
        Assert.Equal("Your Discord and Telegram sides are on different profiles. Resolve the banner first.", outcome.Reason);
        this._svc.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Move_TargetOnTheSameProfile_StillMoves()
    {
        this._humans.Setup(h => h.GetByIdAsync(Telegram)).ReturnsAsync(new Human { Id = Telegram, CurrentProfileNo = 3 });
        this._svc.Setup(s => s.CreateAsync(Telegram, It.IsAny<Monster>())).ReturnsAsync(new Monster { Uid = 77 });
        this._svc.Setup(s => s.DeleteAsync(Discord, 5)).ReturnsAsync(true);
        var m = Model();
        m.ProfileNo = 3;

        Assert.Equal(MoveStatus.Moved, (await this.Move(m)).Status);
    }

    [Fact]
    public async Task Move_PlaceMissingOnTarget_RefusedNothingWritten()
    {
        var m = Model();
        m.OverrideLocationLabel = "Work";

        var outcome = await this.Move(m);

        Assert.Equal(MoveStatus.Refused, outcome.Status);
        this._svc.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Move_PlacePresentOnTarget_MatchesCaseInsensitively()
    {
        this._svc.Setup(s => s.CreateAsync(Telegram, It.IsAny<Monster>())).ReturnsAsync(new Monster { Uid = 77 });
        this._svc.Setup(s => s.DeleteAsync(Discord, 5)).ReturnsAsync(true);
        var m = Model();
        m.OverrideLocationLabel = "home";

        Assert.Equal(MoveStatus.Moved, (await this.Move(m)).Status);
    }

    [Fact]
    public async Task Move_AreaNotPermittedOnTarget_Refused()
    {
        this._policy.Setup(p => p.EnsureOverrideAreasAllowedAsync(Telegram, It.IsAny<IReadOnlyCollection<string>>(), null))
            .ThrowsAsync(new AlarmValidationException("Not allowed: riverside"));
        var m = Model();
        m.OverrideAreas = ["riverside"];

        var outcome = await this.Move(m);

        Assert.Equal(MoveStatus.Refused, outcome.Status);
        Assert.Contains("riverside", outcome.Reason);
        this._svc.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Move_CreateConflict_SourceUntouched()
    {
        this._svc.Setup(s => s.CreateAsync(Telegram, It.IsAny<Monster>()))
            .ThrowsAsync(new TrackingConflictException("pokemon", "Another alarm of this type already uses those settings.") { ExistingUid = 42 });

        var outcome = await this.Move(Model());

        Assert.Equal(MoveStatus.Conflict, outcome.Status);
        Assert.Equal(42, outcome.ExistingUid);
        this._svc.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task Move_IdenticalOnTarget_StillDeletesSource()
    {
        this._svc.Setup(s => s.CreateAsync(Telegram, It.IsAny<Monster>())).ReturnsAsync(new Monster { Uid = 0 });
        this._svc.Setup(s => s.DeleteAsync(Discord, 5)).ReturnsAsync(true);

        var outcome = await this.Move(Model());

        Assert.Equal(MoveStatus.Moved, outcome.Status);
        Assert.Null(outcome.NewUid);
        this._svc.Verify(s => s.DeleteAsync(Discord, 5), Times.Once);
    }

    [Fact]
    public async Task Move_DeleteFailsTwice_SourceRemains()
    {
        this._svc.Setup(s => s.CreateAsync(Telegram, It.IsAny<Monster>())).ReturnsAsync(new Monster { Uid = 77 });
        this._svc.Setup(s => s.DeleteAsync(Discord, 5)).ThrowsAsync(new HttpRequestException("down"));

        var outcome = await this.Move(Model());

        Assert.Equal(MoveStatus.SourceRemains, outcome.Status);
        Assert.Equal(77, outcome.NewUid);
        this._svc.Verify(s => s.DeleteAsync(Discord, 5), Times.Exactly(2));
    }

    [Fact]
    public async Task Move_DeleteFailsOnce_RetriesAndMoves()
    {
        this._svc.Setup(s => s.CreateAsync(Telegram, It.IsAny<Monster>())).ReturnsAsync(new Monster { Uid = 77 });
        this._svc.SetupSequence(s => s.DeleteAsync(Discord, 5)).ThrowsAsync(new HttpRequestException("blip")).ReturnsAsync(true);

        var outcome = await this.Move(Model());

        Assert.Equal(MoveStatus.Moved, outcome.Status);
    }

    [Fact]
    public async Task Move_QuickPickTracking_FollowsTheAlarm()
    {
        this._svc.Setup(s => s.CreateAsync(Telegram, It.IsAny<Monster>())).ReturnsAsync(new Monster { Uid = 77 });
        this._svc.Setup(s => s.DeleteAsync(Discord, 5)).ReturnsAsync(true);
        this._repo.Setup(r => r.GetByUserAndProfileAsync(Discord, 1)).ReturnsAsync(
            [new QuickPickAppliedState { UserId = Discord, ProfileNo = 1, QuickPickId = "p", AlarmType = "monster", TrackedUids = [5] }]);

        await this.Move(Model());

        this._repo.Verify(r => r.CreateOrUpdateAsync(It.Is<QuickPickAppliedState>(s => s.UserId == Telegram && s.TrackedUids.Contains(77))), Times.Once);
    }

    [Fact]
    public async Task MoveMany_UnknownUid_IsRefusedAndTheRestMove()
    {
        this._svc.Setup(s => s.GetByUserAsync(Discord, 1)).ReturnsAsync([Model(5)]);
        this._svc.Setup(s => s.GetByUserAsync(Telegram, 1)).ReturnsAsync([]);
        this._svc.Setup(s => s.CreateAsync(Telegram, It.IsAny<Monster>())).ReturnsAsync(new Monster { Uid = 77 });
        this._svc.Setup(s => s.DeleteAsync(Discord, 5)).ReturnsAsync(true);

        var results = await this._sut.MoveManyAsync<Monster>(this._svc.Object, "pokemon", Discord, 1, [5, 999], LinkSide.Telegram);

        Assert.Equal([MoveStatus.Moved, MoveStatus.Refused], results.Select(r => r.Status));
        this._svc.Verify(s => s.GetByUserAsync(It.IsAny<string>(), 1), Times.Exactly(2));
    }
}
