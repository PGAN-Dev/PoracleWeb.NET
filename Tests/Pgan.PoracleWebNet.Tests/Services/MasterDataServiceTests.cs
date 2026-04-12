using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

public class MasterDataServiceTests : IDisposable
{
    private readonly IMemoryCache _cache;
    private readonly Mock<IHttpClientFactory> _httpClientFactory = new();
    private readonly Mock<ILogger<MasterDataService>> _logger = new();
    private readonly MasterDataService _sut;

    public MasterDataServiceTests()
    {
        this._cache = new MemoryCache(new MemoryCacheOptions());
        this._sut = new MasterDataService(this._cache, this._httpClientFactory.Object, this._logger.Object);
    }

    [Fact]
    public async Task GetPokemonDataAsyncReturnsNullWhenCacheEmptyAndFetchFails()
    {
        // HttpClientFactory returns a client that will fail (no handler set up)
        this._httpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(new HttpClient(new FailingHandler()));

        var result = await this._sut.GetPokemonDataAsync();

        // After failed fetch, cache remains empty
        Assert.Null(result);
    }

    [Fact]
    public async Task GetItemDataAsyncReturnsNullWhenCacheEmptyAndFetchFails()
    {
        this._httpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(new HttpClient(new FailingHandler()));

        var result = await this._sut.GetItemDataAsync();

        Assert.Null(result);
    }

    [Fact]
    public async Task GetPokemonDataAsyncReturnsCachedDataAfterSuccessfulFetch()
    {
        var masterJson = /*lang=json,strict*/ """
        {
            "monsters": {
                "1_0": { "name": "Bulbasaur" },
                "4_0": { "name": "Charmander" }
            },
            "items": {
                "1": { "name": "Poke Ball" }
            }
        }
        """;

        this._httpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(new HttpClient(new FakeHandler(masterJson)));

        var result = await this._sut.GetPokemonDataAsync();

        Assert.NotNull(result);
        Assert.Contains("Bulbasaur", result);
        Assert.Contains("Charmander", result);
    }

    [Fact]
    public async Task GetItemDataAsyncReturnsCachedDataAfterSuccessfulFetch()
    {
        var masterJson = /*lang=json,strict*/ """
        {
            "monsters": {},
            "items": {
                "1": { "name": "Poke Ball" },
                "2": { "name": "Great Ball" }
            }
        }
        """;

        this._httpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(new HttpClient(new FakeHandler(masterJson)));

        var result = await this._sut.GetItemDataAsync();

        Assert.NotNull(result);
        Assert.Contains("Poke Ball", result);
    }

    [Fact]
    public async Task RefreshCacheAsyncHandlesExceptionGracefully()
    {
        this._httpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(new HttpClient(new FailingHandler()));

        // Should not throw
        await this._sut.RefreshCacheAsync();
    }

    [Fact]
    public async Task GetBaseStatsAsyncReturnsStatsForKnownSpecies()
    {
        var masterJson = /*lang=json,strict*/ """
        {
            "monsters": {
                "1_0": { "name": "Bulbasaur", "stats": { "baseAttack": 118, "baseDefense": 111, "baseStamina": 128 } },
                "184_0": { "name": "Azumarill", "stats": { "baseAttack": 112, "baseDefense": 152, "baseStamina": 225 } }
            },
            "items": {}
        }
        """;
        this._httpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(new HttpClient(new FakeHandler(masterJson)));

        var stats = await this._sut.GetBaseStatsAsync(184, 0);

        Assert.NotNull(stats);
        Assert.Equal(112, stats.Value.Attack);
        Assert.Equal(152, stats.Value.Defense);
        Assert.Equal(225, stats.Value.Stamina);
    }

    [Fact]
    public async Task GetBaseStatsAsyncFallsBackToForm0WhenFormSpecificMissing()
    {
        var masterJson = /*lang=json,strict*/ """
        {
            "monsters": {
                "1_0": { "name": "Bulbasaur", "stats": { "baseAttack": 118, "baseDefense": 111, "baseStamina": 128 } }
            },
            "items": {}
        }
        """;
        this._httpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(new HttpClient(new FakeHandler(masterJson)));

        var stats = await this._sut.GetBaseStatsAsync(1, 253); // unknown form, should fall back

        Assert.NotNull(stats);
        Assert.Equal(118, stats.Value.Attack);
    }

    [Fact]
    public async Task GetBaseStatsAsyncReturnsNullForUnknownSpecies()
    {
        var masterJson = /*lang=json,strict*/ """
        {
            "monsters": {
                "1_0": { "name": "Bulbasaur", "stats": { "baseAttack": 118, "baseDefense": 111, "baseStamina": 128 } }
            },
            "items": {}
        }
        """;
        this._httpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(new HttpClient(new FakeHandler(masterJson)));

        var stats = await this._sut.GetBaseStatsAsync(9999, 0);

        Assert.Null(stats);
    }

    [Fact]
    public async Task GetBaseStatsAsyncReturnsNullWhenFetchFails()
    {
        this._httpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(new HttpClient(new FailingHandler()));

        var stats = await this._sut.GetBaseStatsAsync(1, 0);

        Assert.Null(stats);
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => throw new HttpRequestException("Network error");
    }

    private sealed class FakeHandler(string responseJson) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson, System.Text.Encoding.UTF8, "application/json")
        });
    }

    public void Dispose() => GC.SuppressFinalize(this);
}
