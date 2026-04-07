using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

public class PokemonAvailabilityServiceTests : IDisposable
{
    private readonly MemoryCache _cache;
    private readonly Mock<IGolbatApiProxy> _golbatProxy = new();
    private readonly PokemonAvailabilityService _sut;

    public PokemonAvailabilityServiceTests()
    {
        this._cache = new MemoryCache(new MemoryCacheOptions());
        this._sut = new PokemonAvailabilityService(
            this._golbatProxy.Object,
            this._cache,
            NullLogger<PokemonAvailabilityService>.Instance);
    }

    [Fact]
    public async Task GetAvailablePokemonIdsAsyncFirstCallCallsProxyAndReturnsData()
    {
        var expected = new List<int> { 1, 4, 7 }.AsReadOnly();
        this._golbatProxy.Setup(p => p.GetAvailablePokemonAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var result = await this._sut.GetAvailablePokemonIdsAsync();

        Assert.Equal(expected, result);
        this._golbatProxy.Verify(p => p.GetAvailablePokemonAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAvailablePokemonIdsAsyncCachedResultDoesNotCallProxyAgain()
    {
        var expected = new List<int> { 1, 4, 7 }.AsReadOnly();
        this._golbatProxy.Setup(p => p.GetAvailablePokemonAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var first = await this._sut.GetAvailablePokemonIdsAsync();
        var second = await this._sut.GetAvailablePokemonIdsAsync();

        Assert.Equal(expected, first);
        Assert.Equal(expected, second);
        this._golbatProxy.Verify(p => p.GetAvailablePokemonAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAvailablePokemonIdsAsyncProxyThrowsReturnsEmptyList()
    {
        this._golbatProxy.Setup(p => p.GetAvailablePokemonAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var result = await this._sut.GetAvailablePokemonIdsAsync();

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetAvailablePokemonIdsAsyncProxyThrowsAfterSuccessReturnsLastKnownGood()
    {
        var expected = new List<int> { 25, 150 }.AsReadOnly();

        // First call succeeds
        this._golbatProxy.Setup(p => p.GetAvailablePokemonAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var first = await this._sut.GetAvailablePokemonIdsAsync();
        Assert.Equal(expected, first);

        // Evict the cache to simulate expiration
        this._cache.Remove("golbat_available_pokemon");

        // Second call fails
        this._golbatProxy.Setup(p => p.GetAvailablePokemonAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var second = await this._sut.GetAvailablePokemonIdsAsync();

        Assert.Equal(expected, second);
    }

    [Fact]
    public async Task GetAvailablePokemonIdsAsyncCacheExpiresCallsProxyAgain()
    {
        var firstData = new List<int> { 1, 2, 3 }.AsReadOnly();
        var secondData = new List<int> { 4, 5, 6 }.AsReadOnly();

        this._golbatProxy.SetupSequence(p => p.GetAvailablePokemonAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(firstData)
            .ReturnsAsync(secondData);

        var first = await this._sut.GetAvailablePokemonIdsAsync();
        Assert.Equal(firstData, first);

        // Evict the cache to simulate expiration
        this._cache.Remove("golbat_available_pokemon");

        var second = await this._sut.GetAvailablePokemonIdsAsync();
        Assert.Equal(secondData, second);

        this._golbatProxy.Verify(p => p.GetAvailablePokemonAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    public void Dispose()
    {
        this._cache.Dispose();
        GC.SuppressFinalize(this);
    }
}
