using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

public class UserAreaWriterTests
{
    private const string Human = "u1";
    private readonly Mock<IPoracleHumanProxy> _proxy = new();
    private readonly Mock<IUserGeofenceRepository> _fences = new();
    private readonly UserAreaWriter _sut;

    public UserAreaWriterTests()
    {
        this._sut = new UserAreaWriter(this._proxy.Object, this._fences.Object);
        this._proxy.Setup(p => p.GetAreaListAsync(Human)).ReturnsAsync([]);
        this._proxy.Setup(p => p.GetAvailableAreasAsync(Human)).ReturnsAsync([]);
        this._proxy.Setup(p => p.SetAreasTrustedAsync(Human, It.IsAny<IReadOnlyCollection<string>>())).ReturnsAsync([]);
        this._fences.Setup(r => r.GetByHumanIdAsync(Human)).ReturnsAsync([]);
    }

    private void Selectable(params string[] names) =>
        this._proxy.Setup(p => p.GetAvailableAreasAsync(Human))
            .ReturnsAsync(names.Select(n => new V2AreaInfo(n, true, false)).ToList());

    private void NonSelectable(params string[] names) =>
        this._proxy.Setup(p => p.GetAvailableAreasAsync(Human))
            .ReturnsAsync(names.Select(n => new V2AreaInfo(n, false, false)).ToList());

    private void Owns(params string[] names) =>
        this._fences.Setup(r => r.GetByHumanIdAsync(Human))
            .ReturnsAsync(names.Select((n, i) => new UserGeofence { Id = i + 1, HumanId = Human, KojiName = n }).ToList());

    private void Holds(params string[] names) =>
        this._proxy.Setup(p => p.GetAreaListAsync(Human)).ReturnsAsync(names);


    private void VerifyNothingPosted() =>
        this._proxy.Verify(p => p.SetAreasTrustedAsync(It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>()), Times.Never);

    // --- refusals ---

    [Fact]
    public async Task RefusesAnotherUsersFenceAndPostsNothing()
    {
        this.Selectable("downtown");

        var ex = await Assert.ThrowsAsync<AreaNotPermittedException>(
            () => this._sut.SaveSelectionAsync(Human, ["Downtown", "Other Fence"]));

        Assert.Equal(["other fence"], ex.Names);
        this.VerifyNothingPosted();
    }

    [Fact]
    public async Task RefusesANonSelectableAdminFenceTheUserDoesNotHold()
    {
        this.NonSelectable("staff only");

        var ex = await Assert.ThrowsAsync<AreaNotPermittedException>(
            () => this._sut.SaveSelectionAsync(Human, ["staff only"]));

        Assert.Equal(["staff only"], ex.Names);
        this.VerifyNothingPosted();
    }

    // --- legitimate cases still pass ---

    [Fact]
    public async Task PostsSelectableOwnAndAlreadyHeldNamesTrusted()
    {
        this.Selectable("downtown");
        this.Owns("my park");
        this.Holds("staff only");
        IReadOnlyCollection<string>? sent = null;
        this._proxy.Setup(p => p.SetAreasTrustedAsync(Human, It.IsAny<IReadOnlyCollection<string>>()))
            .Callback<string, IReadOnlyCollection<string>>((_, a) => sent = a)
            .ReturnsAsync([]);

        var result = await this._sut.SaveSelectionAsync(Human, ["  Downtown ", "My Park", "Staff Only"]);

        Assert.Equal(["downtown", "my park", "staff only"], sent);
        Assert.Equal(["downtown", "my park", "staff only"], result.Stored);
        Assert.Empty(result.Dropped);
    }

    [Fact]
    public async Task AnUnknownHeldNameThatPoracleRejectsDoesNotFailTheSave()
    {
        // 69 production names across 30 users have no fence PoracleNG knows (renamed, &-escaped).
        this.Selectable("downtown");
        this.Holds("old & gone");
        this._proxy.Setup(p => p.SetAreasTrustedAsync(Human, It.IsAny<IReadOnlyCollection<string>>()))
            .ReturnsAsync(["old & gone"]);

        var result = await this._sut.SaveSelectionAsync(Human, ["downtown", "old & gone"]);

        Assert.Equal(["old & gone"], result.Dropped);
        Assert.Equal(["downtown"], result.Stored);
    }

    [Fact]
    public async Task UnderscoreAndSpaceSpellingsAreTheSameName()
    {
        // PoracleNG NormalizeAreaKey equates the two, so a held "west_end" authorises "west end".
        this.Holds("west_end");

        var result = await this._sut.SaveSelectionAsync(Human, ["west end"]);

        Assert.Equal(["west end"], result.Stored);
    }

    [Fact]
    public async Task AnEmptySelectionClearsTheProfile()
    {
        var result = await this._sut.SaveSelectionAsync(Human, []);

        Assert.Empty(result.Stored);
        this._proxy.Verify(p => p.SetAreasTrustedAsync(Human, It.Is<IReadOnlyCollection<string>>(a => a.Count == 0)), Times.Once);
    }

    // --- add / remove ---

    [Fact]
    public async Task AddPostsTheListWithTheNameAppended()
    {
        this.Holds("downtown");
        IReadOnlyCollection<string>? sent = null;
        this._proxy.Setup(p => p.SetAreasTrustedAsync(Human, It.IsAny<IReadOnlyCollection<string>>()))
            .Callback<string, IReadOnlyCollection<string>>((_, a) => sent = a)
            .ReturnsAsync([]);

        Assert.True(await this._sut.AddToActiveProfileAsync(Human, "My Park"));
        Assert.Equal(["downtown", "my park"], sent);
    }

    [Fact]
    public async Task AddIsANoOpWhenAlreadyHeld()
    {
        this.Holds("my park");

        Assert.False(await this._sut.AddToActiveProfileAsync(Human, "my park"));
        this.VerifyNothingPosted();
    }

    [Fact]
    public async Task AddReturnsFalseWhenPoracleRejectsTheName()
    {
        this._proxy.Setup(p => p.SetAreasTrustedAsync(Human, It.IsAny<IReadOnlyCollection<string>>()))
            .ReturnsAsync(["my park"]);

        Assert.False(await this._sut.AddToActiveProfileAsync(Human, "my park"));
    }

    [Fact]
    public async Task RemovePostsTheListWithoutTheName()
    {
        this.Holds("downtown", "my park");
        IReadOnlyCollection<string>? sent = null;
        this._proxy.Setup(p => p.SetAreasTrustedAsync(Human, It.IsAny<IReadOnlyCollection<string>>()))
            .Callback<string, IReadOnlyCollection<string>>((_, a) => sent = a)
            .ReturnsAsync([]);

        Assert.True(await this._sut.RemoveFromActiveProfileAsync(Human, "My Park"));
        Assert.Equal(["downtown"], sent);
    }

    [Fact]
    public async Task RemoveIsANoOpWhenNotHeld()
    {
        this.Holds("downtown");

        Assert.False(await this._sut.RemoveFromActiveProfileAsync(Human, "my park"));
        this.VerifyNothingPosted();
    }
}
