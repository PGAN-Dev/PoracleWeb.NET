using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

public class UserAreaPolicyTests
{
    private const string Human = "u1";
    private readonly Mock<IPoracleHumanProxy> _proxy = new();
    private readonly Mock<IUserGeofenceRepository> _fences = new();
    private readonly UserAreaPolicy _sut;

    public UserAreaPolicyTests()
    {
        this._sut = new UserAreaPolicy(this._proxy.Object, this._fences.Object);
        this._proxy.Setup(p => p.GetAreaListAsync(Human)).ReturnsAsync([]);
        this._proxy.Setup(p => p.GetAvailableAreasAsync(Human)).ReturnsAsync([]);
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

    [Fact]
    public async Task SelectableArea_Allowed()
    {
        this.Selectable("downtown");

        await this._sut.EnsureOverrideAreasAllowedAsync(Human, ["downtown"], null);
    }

    [Fact]
    public async Task OwnFence_Allowed()
    {
        this.NonSelectable("my fence");
        this.Owns("my fence");

        await this._sut.EnsureOverrideAreasAllowedAsync(Human, ["my fence"], null);
    }

    [Fact]
    public async Task AreaOnActiveProfile_Allowed()
    {
        this.NonSelectable("legacy");
        this.Holds("legacy");

        await this._sut.EnsureOverrideAreasAllowedAsync(Human, ["legacy"], null);
    }

    [Fact]
    public async Task OtherUsersPrivateFence_Refused()
    {
        this.Selectable("downtown");

        var ex = await Assert.ThrowsAsync<AlarmValidationException>(
            () => this._sut.EnsureOverrideAreasAllowedAsync(Human, ["downtown", "other fence"], null));

        Assert.Contains("other fence", ex.Message);
        Assert.DoesNotContain("downtown", ex.Message);
        Assert.StartsWith("These areas can't be used on an alarm:", ex.Message);
    }

    [Fact]
    public async Task NameStoredOnTheRuleBeingEdited_Allowed()
    {
        this.Selectable("downtown");

        await this._sut.EnsureOverrideAreasAllowedAsync(Human, ["downtown", "retired"], () => Task.FromResult<IReadOnlyCollection<string>>(["Retired"]));
    }

    [Fact]
    public async Task StoredOnRule_StillRefusesWhatTheRuleDoesNotHold()
    {
        var ex = await Assert.ThrowsAsync<AlarmValidationException>(
            () => this._sut.EnsureOverrideAreasAllowedAsync(Human, ["retired", "stranger"], () => Task.FromResult<IReadOnlyCollection<string>>(["retired"])));

        Assert.Contains("stranger", ex.Message);
        Assert.DoesNotContain("retired", ex.Message);
    }

    [Fact]
    public async Task StoredOnRule_NotReadWhenEverythingIsAllowed()
    {
        this.Selectable("downtown");
        var called = false;

        await this._sut.EnsureOverrideAreasAllowedAsync(Human, ["downtown"], () =>
        {
            called = true;
            return Task.FromResult<IReadOnlyCollection<string>>([]);
        });

        Assert.False(called);
    }

    [Fact]
    public async Task CaseAndUnderscores_MatchAsPoracleNGKeysThem()
    {
        this.Selectable("Old Town");

        await this._sut.EnsureOverrideAreasAllowedAsync(Human, ["  OLD_TOWN "], null);
        Assert.Equal("old town", UserAreaPolicy.Key(" Old_Town "));
        Assert.Equal("old_town", UserAreaPolicy.Clean(" Old_Town "));
    }

    [Fact]
    public async Task NothingRequested_ReadsNothing()
    {
        await this._sut.EnsureOverrideAreasAllowedAsync(Human, [], null);

        this._proxy.Verify(p => p.GetAvailableAreasAsync(It.IsAny<string>()), Times.Never);
    }
}
