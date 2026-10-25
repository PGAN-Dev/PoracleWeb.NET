using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// Every refusal a move answers carries a code the SPA translates, so no English sentence written here reaches a
/// person reading another language. An alarm already on the target is its own status: the person got what they
/// asked for, and it must not be counted as "not moved".
/// </summary>
public class AlarmMoverReasonCodeTests
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

    public AlarmMoverReasonCodeTests()
    {
        this._linked.Setup(l => l.ResolveAsync(It.IsAny<string>())).ReturnsAsync(Pair());
        this._humans.Setup(h => h.GetByIdAsync(Telegram)).ReturnsAsync(new Human { Id = Telegram, CurrentProfileNo = 1 });
        this._humans.Setup(h => h.GetByIdAsync(Discord)).ReturnsAsync(new Human { Id = Discord, CurrentProfileNo = 1 });
        this._proxy.Setup(p => p.GetPlacesAsync(Telegram)).ReturnsAsync(new SavedPlaces { Named = [new SavedPlace { Label = "Home" }] });
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

    private static Monster Model(int uid = 5) => new() { Uid = uid, Destination = "discord" };

    private async Task<MoveOutcome> Move(Monster m) =>
        (await this._sut.MoveAsync<Monster>(this._svc.Object, "pokemon", Discord, m, Discord, LinkSide.Telegram)).Outcome;

    [Fact]
    public async Task MoveMany_AlarmAlreadyOnTheTarget_IsAlreadyThereAndWritesNothing()
    {
        this._svc.Setup(s => s.GetByUserAsync(Discord, 1)).ReturnsAsync([]);
        this._svc.Setup(s => s.GetByUserAsync(Telegram, 1)).ReturnsAsync([Model(8)]);

        var results = await this._sut.MoveManyAsync<Monster>(this._svc.Object, "pokemon", Discord, 1, [8], LinkSide.Telegram);

        var outcome = Assert.Single(results);
        Assert.Equal(MoveStatus.AlreadyThere, outcome.Status);
        Assert.Equal(MoveReasonCodes.AlreadyThere, outcome.ReasonCode);
        this._svc.Verify(s => s.CreateAsync(It.IsAny<string>(), It.IsAny<Monster>()), Times.Never);
        this._svc.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task MoveMany_MixedSelection_MovesTheOthersBesideTheOneAlreadyThere()
    {
        this._svc.Setup(s => s.GetByUserAsync(Discord, 1)).ReturnsAsync([Model(5)]);
        this._svc.Setup(s => s.GetByUserAsync(Telegram, 1)).ReturnsAsync([Model(8)]);
        this._svc.Setup(s => s.CreateAsync(Telegram, It.IsAny<Monster>())).ReturnsAsync(new Monster { Uid = 77 });
        this._svc.Setup(s => s.DeleteAsync(Discord, 5)).ReturnsAsync(true);

        var results = await this._sut.MoveManyAsync<Monster>(this._svc.Object, "pokemon", Discord, 1, [5, 8], LinkSide.Telegram);

        Assert.Equal([MoveStatus.Moved, MoveStatus.AlreadyThere], results.Select(r => r.Status));
        Assert.Null(results[0].ReasonCode);
    }

    [Fact]
    public async Task Move_DifferentProfiles_CarriesItsCode()
    {
        this._humans.Setup(h => h.GetByIdAsync(Telegram)).ReturnsAsync(new Human { Id = Telegram, CurrentProfileNo = 2 });

        var outcome = await this.Move(Model());

        Assert.Equal(MoveReasonCodes.DifferentProfiles, outcome.ReasonCode);
    }

    [Fact]
    public async Task Move_PlaceMissing_NamesThePlaceAndTheApp()
    {
        var m = Model();
        m.OverrideLocationLabel = "Work";

        var outcome = await this.Move(m);

        Assert.Equal(MoveReasonCodes.PlaceMissing, outcome.ReasonCode);
        Assert.Equal("Work", outcome.ReasonPlace);
        Assert.Equal("telegram", outcome.ReasonApp);
    }

    [Fact]
    public async Task Move_AreaNotAllowed_CarriesItsCode()
    {
        this._policy.Setup(p => p.EnsureOverrideAreasAllowedAsync(Telegram, It.IsAny<IReadOnlyCollection<string>>(), null))
            .ThrowsAsync(new AlarmValidationException("Not allowed: riverside"));
        var m = Model();
        m.OverrideAreas = ["riverside"];

        Assert.Equal(MoveReasonCodes.AreaNotAllowed, (await this.Move(m)).ReasonCode);
    }

    [Fact]
    public async Task Move_TargetMissing_NamesTheApp()
    {
        this._humans.Setup(h => h.GetByIdAsync(Telegram)).ReturnsAsync((Human?)null);

        var outcome = await this.Move(Model());

        Assert.Equal(MoveReasonCodes.NotSetUp, outcome.ReasonCode);
        Assert.Equal("telegram", outcome.ReasonApp);
    }

    [Fact]
    public async Task Move_RefusedByPoracleNG_IsInvalidWithTheApp()
    {
        this._svc.Setup(s => s.CreateAsync(Telegram, It.IsAny<Monster>())).ThrowsAsync(new PoracleRequestRefusedException("distance too large", 422));

        var outcome = await this.Move(Model());

        Assert.Equal(MoveReasonCodes.Invalid, outcome.ReasonCode);
        Assert.Equal("telegram", outcome.ReasonApp);
    }

    [Fact]
    public async Task MoveMany_UnknownUid_IsNotFound()
    {
        this._svc.Setup(s => s.GetByUserAsync(Discord, 1)).ReturnsAsync([]);
        this._svc.Setup(s => s.GetByUserAsync(Telegram, 1)).ReturnsAsync([]);

        var results = await this._sut.MoveManyAsync<Monster>(this._svc.Object, "pokemon", Discord, 1, [9], LinkSide.Telegram);

        Assert.Equal(MoveReasonCodes.NotFound, Assert.Single(results).ReasonCode);
    }

    [Fact]
    public async Task MoveMany_NotLinked_IsNotLinked()
    {
        this._linked.Setup(l => l.ResolveAsync(It.IsAny<string>())).ReturnsAsync(LinkedPair.Single(Discord));

        var results = await this._sut.MoveManyAsync<Monster>(this._svc.Object, "pokemon", Discord, 1, [9], LinkSide.Telegram);

        Assert.Equal(MoveReasonCodes.NotLinked, Assert.Single(results).ReasonCode);
    }

    [Fact]
    public async Task Move_Clean_HasNoCode()
    {
        this._svc.Setup(s => s.CreateAsync(Telegram, It.IsAny<Monster>())).ReturnsAsync(new Monster { Uid = 77 });
        this._svc.Setup(s => s.DeleteAsync(Discord, 5)).ReturnsAsync(true);

        var outcome = await this.Move(Model());

        Assert.Equal(MoveStatus.Moved, outcome.Status);
        Assert.Null(outcome.ReasonCode);
    }
}
