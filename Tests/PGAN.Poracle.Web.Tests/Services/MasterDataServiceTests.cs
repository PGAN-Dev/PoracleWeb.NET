using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using PGAN.Poracle.Web.Core.Services;

namespace PGAN.Poracle.Web.Tests.Services;

public class MasterDataServiceTests
{
    private readonly IMemoryCache _cache;
    private readonly Mock<IHttpClientFactory> _httpClientFactory = new();
    private readonly Mock<ILogger<MasterDataService>> _logger = new();
    private readonly MasterDataService _sut;

    public MasterDataServiceTests()
    {
        _cache = new MemoryCache(new MemoryCacheOptions());
        _sut = new MasterDataService(_cache, _httpClientFactory.Object, _logger.Object);
    }

    [Fact]
    public async Task GetPokemonDataAsync_ReturnsNull_WhenCacheEmpty_AndFetchFails()
    {
        // HttpClientFactory returns a client that will fail (no handler set up)
        _httpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(new HttpClient(new FailingHandler()));

        var result = await _sut.GetPokemonDataAsync();

        // After failed fetch, cache remains empty
        Assert.Null(result);
    }

    [Fact]
    public async Task GetItemDataAsync_ReturnsNull_WhenCacheEmpty_AndFetchFails()
    {
        _httpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(new HttpClient(new FailingHandler()));

        var result = await _sut.GetItemDataAsync();

        Assert.Null(result);
    }

    [Fact]
    public async Task GetPokemonDataAsync_ReturnsCachedData_AfterSuccessfulFetch()
    {
        var masterJson = """
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

        _httpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(new HttpClient(new FakeHandler(masterJson)));

        var result = await _sut.GetPokemonDataAsync();

        Assert.NotNull(result);
        Assert.Contains("Bulbasaur", result);
        Assert.Contains("Charmander", result);
    }

    [Fact]
    public async Task GetItemDataAsync_ReturnsCachedData_AfterSuccessfulFetch()
    {
        var masterJson = """
        {
            "monsters": {},
            "items": {
                "1": { "name": "Poke Ball" },
                "2": { "name": "Great Ball" }
            }
        }
        """;

        _httpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(new HttpClient(new FakeHandler(masterJson)));

        var result = await _sut.GetItemDataAsync();

        Assert.NotNull(result);
        Assert.Contains("Poke Ball", result);
    }

    [Fact]
    public async Task RefreshCacheAsync_HandlesExceptionGracefully()
    {
        _httpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(new HttpClient(new FailingHandler()));

        // Should not throw
        await _sut.RefreshCacheAsync();
    }

    private class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw new HttpRequestException("Network error");
        }
    }

    private class FakeHandler(string responseJson) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }
}
