using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

public class FeatureGateTests
{
    private readonly Mock<ISiteSettingService> _settings = new();
    private readonly FeatureGate _sut;

    public FeatureGateTests() => this._sut = new FeatureGate(this._settings.Object, NullLogger<FeatureGate>.Instance);

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
}
