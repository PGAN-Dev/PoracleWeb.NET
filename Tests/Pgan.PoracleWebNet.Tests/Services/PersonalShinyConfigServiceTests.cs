using Microsoft.Extensions.Caching.Memory;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// The two PoracleNG settings personal shiny depends on, cached for five minutes. Every fault or
/// absent key reads as "not configured, self-service closed": the safe direction for both.
/// </summary>
public class PersonalShinyConfigServiceTests : IDisposable
{
    private readonly Mock<IPoracleApiProxy> _apiProxy = new();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly PersonalShinyConfigService _sut;

    public PersonalShinyConfigServiceTests() => this._sut = new PersonalShinyConfigService(this._apiProxy.Object, this._cache);

    public void Dispose()
    {
        this._cache.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task ReadsBothValues()
    {
        this._apiProxy.Setup(p => p.GetShinyRatesUrlAsync()).ReturnsAsync("https://example.test/rates.json");
        this._apiProxy.Setup(p => p.GetPlayerAccountsSelfServiceAsync()).ReturnsAsync(true);

        Assert.Equal(new PersonalShinyConfig(true, true), await this._sut.GetAsync());
    }

    [Fact]
    public async Task CachesTheAnswer()
    {
        this._apiProxy.Setup(p => p.GetShinyRatesUrlAsync()).ReturnsAsync("https://example.test/rates.json");
        this._apiProxy.Setup(p => p.GetPlayerAccountsSelfServiceAsync()).ReturnsAsync(true);

        await this._sut.GetAsync();
        await this._sut.GetAsync();

        this._apiProxy.Verify(p => p.GetShinyRatesUrlAsync(), Times.Once);
        this._apiProxy.Verify(p => p.GetPlayerAccountsSelfServiceAsync(), Times.Once);
    }

    [Fact]
    public async Task AFaultReadsAsOffAndClosed()
    {
        this._apiProxy.Setup(p => p.GetShinyRatesUrlAsync()).ThrowsAsync(new HttpRequestException("down"));
        this._apiProxy.Setup(p => p.GetPlayerAccountsSelfServiceAsync()).ReturnsAsync(true);

        // A failed read of one value must not leave the other open: the whole answer fails closed.
        Assert.Equal(new PersonalShinyConfig(false, false), await this._sut.GetAsync());
    }

    [Fact]
    public async Task AFaultOnSelfServiceAlsoClosesTheRatesFlag()
    {
        this._apiProxy.Setup(p => p.GetShinyRatesUrlAsync()).ReturnsAsync("https://example.test/rates.json");
        this._apiProxy.Setup(p => p.GetPlayerAccountsSelfServiceAsync()).ThrowsAsync(new HttpRequestException("down"));

        Assert.Equal(new PersonalShinyConfig(false, false), await this._sut.GetAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ABlankUrlIsNotConfigured(string? url)
    {
        this._apiProxy.Setup(p => p.GetShinyRatesUrlAsync()).ReturnsAsync(url);
        this._apiProxy.Setup(p => p.GetPlayerAccountsSelfServiceAsync()).ReturnsAsync(true);

        var config = await this._sut.GetAsync();

        Assert.False(config.RatesFeedConfigured);
        Assert.True(config.SelfService);
    }

    [Fact]
    public async Task AnAbsentSelfServiceKeyIsClosed()
    {
        this._apiProxy.Setup(p => p.GetShinyRatesUrlAsync()).ReturnsAsync("https://example.test/rates.json");
        this._apiProxy.Setup(p => p.GetPlayerAccountsSelfServiceAsync()).ReturnsAsync((bool?)null);

        var config = await this._sut.GetAsync();

        Assert.True(config.RatesFeedConfigured);
        Assert.False(config.SelfService);
    }
}
