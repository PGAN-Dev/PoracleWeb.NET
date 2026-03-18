using Microsoft.AspNetCore.Mvc;
using Moq;
using PGAN.Poracle.Web.Api.Controllers;
using PGAN.Poracle.Web.Core.Abstractions.Services;

namespace PGAN.Poracle.Web.Tests.Controllers;

public class MasterDataControllerTests : ControllerTestBase
{
    private readonly Mock<IMasterDataService> _masterDataService = new();
    private readonly Mock<IPoracleApiProxy> _poracleApiProxy = new();
    private readonly MasterDataController _sut;

    public MasterDataControllerTests()
    {
        _sut = new MasterDataController(_masterDataService.Object, _poracleApiProxy.Object);
        SetupUser(_sut);
    }

    // --- GetPokemon ---

    [Fact]
    public async Task GetPokemon_ReturnsContent_WhenCacheHit()
    {
        _masterDataService.Setup(s => s.GetPokemonDataAsync()).ReturnsAsync("{\"1\":\"Bulbasaur\"}");

        var result = await _sut.GetPokemon();

        var content = Assert.IsType<ContentResult>(result);
        Assert.Equal("application/json", content.ContentType);
        Assert.Contains("Bulbasaur", content.Content);
    }

    [Fact]
    public async Task GetPokemon_RefreshesCache_WhenCacheMiss_ThenReturnsContent()
    {
        // First call returns null, after refresh returns data
        var callCount = 0;
        _masterDataService.Setup(s => s.GetPokemonDataAsync())
            .ReturnsAsync(() => ++callCount > 1 ? "{\"1\":\"Bulbasaur\"}" : null);
        _masterDataService.Setup(s => s.RefreshCacheAsync()).Returns(Task.CompletedTask);

        var result = await _sut.GetPokemon();

        var content = Assert.IsType<ContentResult>(result);
        Assert.Contains("Bulbasaur", content.Content);
        _masterDataService.Verify(s => s.RefreshCacheAsync(), Times.Once);
    }

    [Fact]
    public async Task GetPokemon_ReturnsNotFound_WhenCacheMissAndRefreshFails()
    {
        _masterDataService.Setup(s => s.GetPokemonDataAsync()).ReturnsAsync((string?)null);
        _masterDataService.Setup(s => s.RefreshCacheAsync()).Returns(Task.CompletedTask);

        var result = await _sut.GetPokemon();

        Assert.IsType<NotFoundObjectResult>(result);
    }

    // --- GetItems ---

    [Fact]
    public async Task GetItems_ReturnsContent_WhenCacheHit()
    {
        _masterDataService.Setup(s => s.GetItemDataAsync()).ReturnsAsync("{\"1\":\"Poke Ball\"}");
        var result = await _sut.GetItems();
        Assert.IsType<ContentResult>(result);
    }

    [Fact]
    public async Task GetItems_RefreshesCache_WhenCacheMiss_ThenReturnsContent()
    {
        var callCount = 0;
        _masterDataService.Setup(s => s.GetItemDataAsync())
            .ReturnsAsync(() => ++callCount > 1 ? "{\"1\":\"Poke Ball\"}" : null);
        _masterDataService.Setup(s => s.RefreshCacheAsync()).Returns(Task.CompletedTask);

        var result = await _sut.GetItems();

        Assert.IsType<ContentResult>(result);
        _masterDataService.Verify(s => s.RefreshCacheAsync(), Times.Once);
    }

    [Fact]
    public async Task GetItems_ReturnsNotFound_WhenCacheMissAndRefreshFails()
    {
        _masterDataService.Setup(s => s.GetItemDataAsync()).ReturnsAsync((string?)null);
        _masterDataService.Setup(s => s.RefreshCacheAsync()).Returns(Task.CompletedTask);
        Assert.IsType<NotFoundObjectResult>(await _sut.GetItems());
    }

    // --- GetGrunts ---

    [Fact]
    public async Task GetGrunts_ReturnsContent_WhenAvailable()
    {
        _poracleApiProxy.Setup(p => p.GetGruntsAsync()).ReturnsAsync("{\"grunts\":[]}");
        var result = await _sut.GetGrunts();
        Assert.IsType<ContentResult>(result);
    }

    [Fact]
    public async Task GetGrunts_ReturnsNotFound_WhenNull()
    {
        _poracleApiProxy.Setup(p => p.GetGruntsAsync()).ReturnsAsync((string?)null);
        Assert.IsType<NotFoundObjectResult>(await _sut.GetGrunts());
    }
}
