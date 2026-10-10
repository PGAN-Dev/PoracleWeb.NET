using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// Whether PoracleNG's area_security is confirmed off -- the one condition under which setAreas's
/// trusted flag is safe to rely on regardless of whether the target server carries
/// jfberry/PoracleNG#230's community-restriction fix. See #838.
/// </summary>
public class AreaSecurityPolicyServiceTests
{
    private readonly Mock<IPoracleApiProxy> _proxy = new();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    private AreaSecurityPolicyService CreateSut() =>
        new(this._proxy.Object, this._cache, NullLogger<AreaSecurityPolicyService>.Instance);

    [Fact]
    public async Task ReturnsTrueOnlyWhenPoracleExplicitlyReportsDisabled()
    {
        this._proxy.Setup(p => p.GetAreaSecurityEnabledAsync()).ReturnsAsync(false);

        Assert.True(await this.CreateSut().IsConfirmedDisabledAsync());
    }

    [Fact]
    public async Task ReturnsFalseWhenPoracleReportsItEnabled()
    {
        this._proxy.Setup(p => p.GetAreaSecurityEnabledAsync()).ReturnsAsync(true);

        Assert.False(await this.CreateSut().IsConfirmedDisabledAsync());
    }

    [Fact]
    public async Task FailsClosedWhenTheValueCannotBeDetermined()
    {
        // null means "cannot tell" -- an older Poracle, PoracleJS, or an endpoint shape change. This
        // must read the same as "enabled", not as "disabled".
        this._proxy.Setup(p => p.GetAreaSecurityEnabledAsync()).ReturnsAsync((bool?)null);

        Assert.False(await this.CreateSut().IsConfirmedDisabledAsync());
    }

    [Fact]
    public async Task FailsClosedWhenTheProxyThrows()
    {
        // This is the opposite fail direction from IUpstreamFeatureFlagService, which fails open for
        // an unrelated reason. An unreachable Poracle here must not be read as "safe to bypass a
        // community's allowed-area restriction".
        this._proxy.Setup(p => p.GetAreaSecurityEnabledAsync()).ThrowsAsync(new HttpRequestException("down"));

        Assert.False(await this.CreateSut().IsConfirmedDisabledAsync());
    }

    [Fact]
    public async Task CachesTheAnswerAcrossCalls()
    {
        this._proxy.Setup(p => p.GetAreaSecurityEnabledAsync()).ReturnsAsync(false);
        var sut = this.CreateSut();

        Assert.True(await sut.IsConfirmedDisabledAsync());
        Assert.True(await sut.IsConfirmedDisabledAsync());

        this._proxy.Verify(p => p.GetAreaSecurityEnabledAsync(), Times.Once);
    }
}
