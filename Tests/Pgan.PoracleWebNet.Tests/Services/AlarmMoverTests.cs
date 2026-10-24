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
        this._proxy.Setup(p => p.GetPlacesAsync(Telegram)).ReturnsAsync(new SavedPlaces { Named = [new SavedPlace { Label = "Home" }] });
        this._proxy.Setup(p => p.GetPlacesAsync(Discord)).ReturnsAsync(new SavedPlaces { Named = [new SavedPlace { Label = "Work" }] });
        this._repo.Setup(r => r.GetByUserAndProfileAsync(It.IsAny<string>(), It.IsAny<int>())).ReturnsAsync([]);
        this._sut = new AlarmMover(
            this._linked.Object,
            this._humans.Object,
            this._proxy.Object,
            Mock.Of<IPoracleTrackingProxy>(),
            this._policy.Object,
            new QuickPickUidTransfer(this._repo.Object, NullLogger<QuickPickUidTransfer>.Instance),
            NullLogger<AlarmMover>.Instance);
    }

    private static LinkedPair Pair() => new(
        Discord,
        new AccountLink(1, Discord, Telegram, LinkSide.Discord, null, null, DateTime.UtcNow),
        new PairSide(LinkSide.Discord, Discord, true, PauseReason.None),
        new PairSide(LinkSide.Telegram, Telegram, true, PauseReason.None));

    // ProfileNo stays 0: a v2 read never stamps it, so the mover must not rely on it.
    private static Monster Model(int uid = 5) => new() { Uid = uid, Destination = "discord" };

    private async Task<MoveOutcome> Move(Monster m) =>
        (await this._sut.MoveAsync<Monster>(this._svc.Object, "pokemon", Discord, m, Discord, LinkSide.Telegram)).Outcome;

    [Fact]
    public async Task Move_HappyPath_CreatesThenDeletes()
    {
        var seq = new MockSequence();
        this._svc.InSequence(seq).Setup(s => s.CreateAsync(Telegram, It.IsAny<Monster>())).ReturnsAsync(new Monster { Uid = 77 });
        this._svc.InSequence(seq).Setup(s => s.DeleteAsync(Discord, 5)).ReturnsAsync(true);

        var outcome = await this.Move(Model());

        Assert.Equal(new MoveOutcome(5, MoveStatus.Moved, 77), outcome);
        this._svc.Verify(s => s.DeleteAsync(Discord, 5), Times.Once);
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
        this._humans.Setup(h => h.GetByIdAsync(Discord)).ReturnsAsync(new Human { Id = Discord, CurrentProfileNo = 3 });
        this._svc.Setup(s => s.CreateAsync(Telegram, It.IsAny<Monster>())).ReturnsAsync(new Monster { Uid = 77 });
        this._svc.Setup(s => s.DeleteAsync(Discord, 5)).ReturnsAsync(true);
        var m = Model();

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
    public async Task Move_IdenticalOnTarget_StillDeletesSource_AndMapsOntoTheExistingRule()
    {
        // Services hand back the model they were given when nothing was written, so a leftover source uid would read as created.
        this._svc.Setup(s => s.CreateAsync(Telegram, It.Is<Monster>(m => m.Uid == 0)))
            .ReturnsAsync((string _, Monster m) => { m.ExistingUid = 42; return m; });
        this._svc.Setup(s => s.DeleteAsync(Discord, 5)).ReturnsAsync(true);
        this._repo.Setup(r => r.GetByUserAndProfileAsync(Discord, 1)).ReturnsAsync(
            [new QuickPickAppliedState { UserId = Discord, ProfileNo = 1, QuickPickId = "p", AlarmType = "monster", TrackedUids = [5] }]);

        var result = await this._sut.MoveAsync<Monster>(this._svc.Object, "pokemon", Discord, Model(), Discord, LinkSide.Telegram);

        Assert.Equal(new MoveOutcome(5, MoveStatus.Moved, 42), result.Outcome);
        Assert.Equal(42, result.Created!.Uid);
        this._svc.Verify(s => s.DeleteAsync(Discord, 5), Times.Once);
        this._repo.Verify(r => r.CreateOrUpdateAsync(It.Is<QuickPickAppliedState>(s => s.UserId == Telegram && s.TrackedUids.Contains(42))), Times.Once);
    }

    [Fact]
    public async Task Move_IdenticalReportedAsIdenticalConflict_IsTreatedAsMoved()
    {
        this._svc.Setup(s => s.CreateAsync(Telegram, It.IsAny<Monster>()))
            .ThrowsAsync(new TrackingConflictException("maxbattle", "You already have an identical max battle alarm.") { ExistingUid = 42, IsIdentical = true });
        this._svc.Setup(s => s.DeleteAsync(Discord, 5)).ReturnsAsync(true);

        var outcome = await this.Move(Model());

        Assert.Equal(new MoveOutcome(5, MoveStatus.Moved, 42), outcome);
    }

    [Fact]
    public async Task Move_IdenticalWithNoNamedRule_StillDeletesSource()
    {
        this._svc.Setup(s => s.CreateAsync(Telegram, It.IsAny<Monster>())).ReturnsAsync(new Monster { Uid = 0 });
        this._svc.Setup(s => s.DeleteAsync(Discord, 5)).ReturnsAsync(true);

        var outcome = await this.Move(Model());

        Assert.Equal(new MoveOutcome(5, MoveStatus.Moved), outcome);
    }

    [Fact]
    public async Task Move_CreateRefusedByPoracleNG_IsRefusedWithItsDetail()
    {
        this._svc.Setup(s => s.CreateAsync(Telegram, It.IsAny<Monster>())).ThrowsAsync(new PoracleRequestRefusedException("distance too large", 422));

        var outcome = await this.Move(Model());

        Assert.Equal(MoveStatus.Refused, outcome.Status);
        Assert.Equal("distance too large", outcome.Reason);
        this._svc.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task Move_CreateThrowsUnexpectedly_IsFailedWithFixedText()
    {
        this._svc.Setup(s => s.CreateAsync(Telegram, It.IsAny<Monster>())).ThrowsAsync(new InvalidOperationException("secret internals"));

        var outcome = await this.Move(Model());

        Assert.Equal(MoveStatus.Failed, outcome.Status);
        Assert.DoesNotContain("secret", outcome.Reason);
        this._svc.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task Move_PoracleUnreachableOnCreate_Propagates()
    {
        this._svc.Setup(s => s.CreateAsync(Telegram, It.IsAny<Monster>())).ThrowsAsync(new HttpRequestException("down"));

        await Assert.ThrowsAsync<HttpRequestException>(() => this.Move(Model()));
    }

    [Fact]
    public async Task MoveMany_PoracleUnreachable_PropagatesInsteadOfNRefusedRows()
    {
        this._svc.Setup(s => s.GetByUserAsync(Discord, 1)).ReturnsAsync([Model(5), Model(6)]);
        this._svc.Setup(s => s.GetByUserAsync(Telegram, 1)).ReturnsAsync([]);
        this._svc.Setup(s => s.CreateAsync(Telegram, It.IsAny<Monster>())).ThrowsAsync(new HttpRequestException("down"));

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            this._sut.MoveManyAsync<Monster>(this._svc.Object, "pokemon", Discord, 1, [5, 6], LinkSide.Telegram));
    }

    [Fact]
    public async Task MoveMany_OneUnexpectedFailure_IsFailedAndTheRestStillReport()
    {
        this._svc.Setup(s => s.GetByUserAsync(Discord, 1)).ReturnsAsync([Model(5), Model(6)]);
        this._svc.Setup(s => s.GetByUserAsync(Telegram, 1)).ReturnsAsync([]);
        this._svc.SetupSequence(s => s.CreateAsync(Telegram, It.IsAny<Monster>()))
            .ThrowsAsync(new InvalidOperationException("x")).ReturnsAsync(new Monster { Uid = 77 });
        this._svc.Setup(s => s.DeleteAsync(Discord, 6)).ReturnsAsync(true);

        var results = await this._sut.MoveManyAsync<Monster>(this._svc.Object, "pokemon", Discord, 1, [5, 6], LinkSide.Telegram);

        Assert.Equal([MoveStatus.Failed, MoveStatus.Moved], results.Select(r => r.Status));
    }

    [Fact]
    public async Task MoveMany_ThreeUids_ResolvesThePairAndReadsEachHumanAndPlacesOnce()
    {
        Monster WithPlace(int uid)
        {
            var m = Model(uid);
            m.OverrideLocationLabel = "Home";
            return m;
        }

        this._svc.Setup(s => s.GetByUserAsync(Discord, 1)).ReturnsAsync([WithPlace(5), WithPlace(6), WithPlace(7)]);
        this._svc.Setup(s => s.GetByUserAsync(Telegram, 1)).ReturnsAsync([]);
        this._svc.Setup(s => s.CreateAsync(Telegram, It.IsAny<Monster>())).ReturnsAsync(new Monster { Uid = 77 });
        this._svc.Setup(s => s.DeleteAsync(Discord, It.IsAny<int>())).ReturnsAsync(true);

        var results = await this._sut.MoveManyAsync<Monster>(this._svc.Object, "pokemon", Discord, 1, [5, 6, 7], LinkSide.Telegram);

        Assert.All(results, r => Assert.Equal(MoveStatus.Moved, r.Status));
        this._linked.Verify(l => l.ResolveAsync(It.IsAny<string>()), Times.Once);
        this._humans.Verify(h => h.GetByIdAsync(Discord), Times.Once);
        this._humans.Verify(h => h.GetByIdAsync(Telegram), Times.Once);
        this._proxy.Verify(p => p.GetPlacesAsync(Telegram), Times.Once);
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
    public async Task Move_ModelProfileZero_BothHumansOnProfile1_MovesAndTransfersOnProfile1()
    {
        this._svc.Setup(s => s.CreateAsync(Telegram, It.IsAny<Monster>())).ReturnsAsync(new Monster { Uid = 77 });
        this._svc.Setup(s => s.DeleteAsync(Discord, 5)).ReturnsAsync(true);
        this._repo.Setup(r => r.GetByUserAndProfileAsync(Discord, 1)).ReturnsAsync(
            [new QuickPickAppliedState { UserId = Discord, ProfileNo = 1, QuickPickId = "p", AlarmType = "monster", TrackedUids = [5] }]);

        var outcome = await this.Move(Model());

        Assert.Equal(MoveStatus.Moved, outcome.Status);
        this._repo.Verify(r => r.CreateOrUpdateAsync(It.Is<QuickPickAppliedState>(s => s.UserId == Telegram && s.ProfileNo == 1 && s.TrackedUids.Contains(77))), Times.Once);
        this._repo.Verify(r => r.GetByUserAndProfileAsync(It.IsAny<string>(), 0), Times.Never);
    }

    [Fact]
    public async Task Move_SourceAndTargetOnProfile2_MovesAndTransfersOnProfile2()
    {
        this._humans.Setup(h => h.GetByIdAsync(Telegram)).ReturnsAsync(new Human { Id = Telegram, CurrentProfileNo = 2 });
        this._humans.Setup(h => h.GetByIdAsync(Discord)).ReturnsAsync(new Human { Id = Discord, CurrentProfileNo = 2 });
        this._svc.Setup(s => s.CreateAsync(Telegram, It.IsAny<Monster>())).ReturnsAsync(new Monster { Uid = 77 });
        this._svc.Setup(s => s.DeleteAsync(Discord, 5)).ReturnsAsync(true);

        Assert.Equal(MoveStatus.Moved, (await this.Move(Model())).Status);
        this._repo.Verify(r => r.GetByUserAndProfileAsync(Discord, 2), Times.Once);
    }

    [Fact]
    public async Task Move_DeleteReportsNotFound_CountsAsGone()
    {
        this._svc.Setup(s => s.CreateAsync(Telegram, It.IsAny<Monster>())).ReturnsAsync(new Monster { Uid = 77 });
        this._svc.Setup(s => s.DeleteAsync(Discord, 5)).ReturnsAsync(false);

        Assert.Equal(MoveStatus.Moved, (await this.Move(Model())).Status);
    }

    [Fact]
    public async Task Move_NullTargetHuman_RefusedNothingWritten()
    {
        this._humans.Setup(h => h.GetByIdAsync(Telegram)).ReturnsAsync((Human?)null);

        Assert.Equal(MoveStatus.Refused, (await this.Move(Model())).Status);
        this._svc.VerifyNoOtherCalls();
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

    [Fact]
    public async Task Move_TypeSwitchedOffOnCreate_ReachesItsOwnFilter()
    {
        this._svc.Setup(s => s.CreateAsync(Telegram, It.IsAny<Monster>())).ThrowsAsync(new FeatureDisabledException(DisableFeatureKeys.Pokemon));

        await Assert.ThrowsAsync<FeatureDisabledException>(() => this.Move(Model()));
        this._svc.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task Move_PoracleTooOldOnCreate_ReachesItsOwnFilter()
    {
        this._svc.Setup(s => s.CreateAsync(Telegram, It.IsAny<Monster>())).ThrowsAsync(new PoracleUnsupportedException("saving alarms", "PoracleNG 5.4.0"));

        await Assert.ThrowsAsync<PoracleUnsupportedException>(() => this.Move(Model()));
        this._svc.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task Move_NullSourceHuman_NamesTheSourceSide()
    {
        this._humans.Setup(h => h.GetByIdAsync(Discord)).ReturnsAsync((Human?)null);

        var outcome = await this.Move(Model());

        Assert.Equal(MoveStatus.Refused, outcome.Status);
        Assert.Equal("Discord isn't set up yet.", outcome.Reason);
        this._svc.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Move_NullTargetHuman_NamesTheTargetSide()
    {
        this._humans.Setup(h => h.GetByIdAsync(Telegram)).ReturnsAsync((Human?)null);

        Assert.Equal("Telegram isn't set up yet.", (await this.Move(Model())).Reason);
    }

    [Fact]
    public async Task Move_DeleteThrowsSomethingADeleteNeverDoes_Propagates()
    {
        this._svc.Setup(s => s.CreateAsync(Telegram, It.IsAny<Monster>())).ReturnsAsync(new Monster { Uid = 77 });
        // The strict mock has no DeleteAsync setup, so the call throws Moq's own MockException. Swallowing it
        // would report "source remains" and hide a test that never set the delete up.

        await Assert.ThrowsAnyAsync<MockException>(() => this.Move(Model()));
    }

    [Theory]
    [MemberData(nameof(DeleteFailures))]
    public async Task Move_DeleteRefusedByPoracle_SourceRemains(Exception failure)
    {
        this._svc.Setup(s => s.CreateAsync(Telegram, It.IsAny<Monster>())).ReturnsAsync(new Monster { Uid = 77 });
        this._svc.Setup(s => s.DeleteAsync(Discord, 5)).ThrowsAsync(failure);

        Assert.Equal(MoveStatus.SourceRemains, (await this.Move(Model())).Status);
    }

    public static TheoryData<Exception> DeleteFailures() => new()
    {
        new PoracleRequestRefusedException("refused", 422),
        new PoracleAmbiguousAnswerException(),
        new PoracleUnsupportedException("Deleting alarms", "PoracleNG 5.4.0"),
    };
}
