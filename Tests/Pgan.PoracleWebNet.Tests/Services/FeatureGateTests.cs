using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

public class FeatureGateTests
{
    private readonly Mock<ISiteSettingService> _settings = new();
    private readonly Mock<IUpstreamFeatureFlagService> _upstreamFlags = new();
    private readonly FeatureGate _sut;

    public FeatureGateTests()
    {
        // The prod default: Poracle reports "disabledHooks": [], so nothing is forced off upstream
        // and the site settings are in sole charge. Individual tests override this.
        this._upstreamFlags
            .Setup(f => f.GetDisabledKeysAsync())
            .ReturnsAsync(new HashSet<string>(StringComparer.Ordinal));

        this._sut = new FeatureGate(this._settings.Object, this._upstreamFlags.Object, NullLogger<FeatureGate>.Instance);
    }

    private void UpstreamDisables(params string[] keys) =>
        this._upstreamFlags
            .Setup(f => f.GetDisabledKeysAsync())
            .ReturnsAsync(new HashSet<string>(keys, StringComparer.Ordinal));

    [Fact]
    public async Task IsEnabledReturnsTrueWhenSettingFalse()
    {
        this._settings.Setup(s => s.GetBoolAsync("disable_mons")).ReturnsAsync(false);

        Assert.True(await this._sut.IsEnabledAsync("disable_mons"));
    }

    [Fact]
    public async Task IsEnabledReturnsFalseWhenSettingTrue()
    {
        this._settings.Setup(s => s.GetBoolAsync("disable_mons")).ReturnsAsync(true);

        Assert.False(await this._sut.IsEnabledAsync("disable_mons"));
    }

    [Fact]
    public async Task EnsureEnabledIsNoOpWhenEnabled()
    {
        this._settings.Setup(s => s.GetBoolAsync("disable_mons")).ReturnsAsync(false);

        await this._sut.EnsureEnabledAsync("disable_mons");
    }

    [Fact]
    public async Task EnsureEnabledThrowsFeatureDisabledExceptionWithKey()
    {
        this._settings.Setup(s => s.GetBoolAsync("disable_mons")).ReturnsAsync(true);

        var ex = await Assert.ThrowsAsync<FeatureDisabledException>(() => this._sut.EnsureEnabledAsync("disable_mons"));
        Assert.Equal("disable_mons", ex.DisableKey);
    }

    // --- Poracle's own flags act as a floor under the site settings (#769) ---

    /// <summary>
    /// The whole point: the site setting says the type is on, Poracle says it is off, and Poracle
    /// wins. Its processor drops the webhook and its bot refuses the command, so an alarm created
    /// here could only ever save and then never fire.
    /// </summary>
    [Fact]
    public async Task IsEnabledReturnsFalseWhenPoracleDisablesTheTypeAndTheSiteSettingDoesNot()
    {
        this._settings.Setup(s => s.GetBoolAsync(DisableFeatureKeys.Raids)).ReturnsAsync(false);
        this.UpstreamDisables(DisableFeatureKeys.Raids);

        Assert.False(await this._sut.IsEnabledAsync(DisableFeatureKeys.Raids));
    }

    [Fact]
    public async Task EnsureEnabledThrowsWithTheSameKeyWhenPoracleDisablesTheType()
    {
        this._settings.Setup(s => s.GetBoolAsync(DisableFeatureKeys.Quests)).ReturnsAsync(false);
        this.UpstreamDisables(DisableFeatureKeys.Quests);

        var ex = await Assert.ThrowsAsync<FeatureDisabledException>(
            () => this._sut.EnsureEnabledAsync(DisableFeatureKeys.Quests));

        // The SPA's 403 interceptor keys off disableKey, so the wire format must not differ by source.
        Assert.Equal(DisableFeatureKeys.Quests, ex.DisableKey);
    }

    /// <summary>
    /// A floor, not a switch: Poracle disabling raids must not re-enable anything else, and must not
    /// touch the keys it has no opinion about at all (areas, profiles, location, geocoding).
    /// </summary>
    [Theory]
    [InlineData(DisableFeatureKeys.Pokemon)]
    [InlineData(DisableFeatureKeys.Quests)]
    [InlineData(DisableFeatureKeys.Invasions)]
    [InlineData(DisableFeatureKeys.Lures)]
    [InlineData(DisableFeatureKeys.Nests)]
    [InlineData(DisableFeatureKeys.Gyms)]
    [InlineData(DisableFeatureKeys.MaxBattles)]
    [InlineData(DisableFeatureKeys.FortChanges)]
    [InlineData(DisableFeatureKeys.Areas)]
    [InlineData(DisableFeatureKeys.Profiles)]
    [InlineData(DisableFeatureKeys.Location)]
    [InlineData(DisableFeatureKeys.Geocoding)]
    [InlineData(DisableFeatureKeys.UserGeofences)]
    public async Task OneUpstreamDisabledTypeLeavesEveryOtherKeyAlone(string key)
    {
        this._settings.Setup(s => s.GetBoolAsync(It.IsAny<string>())).ReturnsAsync(false);
        this.UpstreamDisables(DisableFeatureKeys.Raids);

        Assert.True(await this._sut.IsEnabledAsync(key));
    }

    /// <summary>
    /// What prod actually serves — <c>"disabledHooks": []</c> — must leave every type creatable.
    /// This is the half that catches the regression, not the refusal tests above.
    /// </summary>
    [Theory]
    [InlineData(DisableFeatureKeys.Pokemon)]
    [InlineData(DisableFeatureKeys.Raids)]
    [InlineData(DisableFeatureKeys.Quests)]
    [InlineData(DisableFeatureKeys.Invasions)]
    [InlineData(DisableFeatureKeys.Lures)]
    [InlineData(DisableFeatureKeys.Nests)]
    [InlineData(DisableFeatureKeys.Gyms)]
    [InlineData(DisableFeatureKeys.MaxBattles)]
    [InlineData(DisableFeatureKeys.FortChanges)]
    public async Task EveryAlarmTypeStaysEnabledWhenPoracleDisablesNothing(string key)
    {
        this._settings.Setup(s => s.GetBoolAsync(It.IsAny<string>())).ReturnsAsync(false);

        Assert.True(await this._sut.IsEnabledAsync(key));
        await this._sut.EnsureEnabledAsync(key);
    }

    /// <summary>
    /// The site setting keeps working on its own. Poracle's flags are additive — they never enable
    /// something an admin has switched off here.
    /// </summary>
    [Fact]
    public async Task SiteSettingStillDisablesWhenPoracleDisablesNothing()
    {
        this._settings.Setup(s => s.GetBoolAsync(DisableFeatureKeys.Lures)).ReturnsAsync(true);

        Assert.False(await this._sut.IsEnabledAsync(DisableFeatureKeys.Lures));
    }

    /// <summary>
    /// The cheap check runs first and short-circuits: an admin-disabled feature must not cost an
    /// upstream HTTP round-trip on every gated request.
    /// </summary>
    [Fact]
    public async Task DoesNotConsultPoracleWhenTheSiteSettingAlreadyDisablesTheFeature()
    {
        this._settings.Setup(s => s.GetBoolAsync(DisableFeatureKeys.Gyms)).ReturnsAsync(true);

        await Assert.ThrowsAsync<FeatureDisabledException>(() => this._sut.EnsureEnabledAsync(DisableFeatureKeys.Gyms));

        this._upstreamFlags.Verify(f => f.GetDisabledKeysAsync(), Times.Never);
    }
}
