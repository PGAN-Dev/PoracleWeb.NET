using Microsoft.AspNetCore.Mvc;
using Moq;
using Pgan.PoracleWebNet.Api.Controllers;
using Pgan.PoracleWebNet.Core.Abstractions.Services;

namespace Pgan.PoracleWebNet.Tests.Controllers;

public class MasterDataControllerTests : ControllerTestBase
{
    private readonly Mock<IMasterDataService> _masterDataService = new();
    private readonly Mock<IPoracleApiProxy> _poracleApiProxy = new();
    private readonly MasterDataController _sut;

    public MasterDataControllerTests()
    {
        this._sut = new MasterDataController(this._masterDataService.Object, this._poracleApiProxy.Object);
        SetupUser(this._sut);
    }

    // --- GetPokemon ---

    [Fact]
    public async Task GetPokemonReturnsContentWhenCacheHit()
    {
        this._masterDataService.Setup(s => s.GetPokemonDataAsync()).ReturnsAsync(/*lang=json,strict*/ "{\"1\":\"Bulbasaur\"}");

        var result = await this._sut.GetPokemon();

        var content = Assert.IsType<ContentResult>(result);
        Assert.Equal("application/json", content.ContentType);
        Assert.Contains("Bulbasaur", content.Content);
    }

    [Fact]
    public async Task GetPokemonRefreshesCacheWhenCacheMissThenReturnsContent()
    {
        // First call returns null, after refresh returns data
        var callCount = 0;
        this._masterDataService.Setup(s => s.GetPokemonDataAsync())
            .ReturnsAsync(() => ++callCount > 1 ? /*lang=json,strict*/ "{\"1\":\"Bulbasaur\"}" : null);
        this._masterDataService.Setup(s => s.RefreshCacheAsync()).Returns(Task.CompletedTask);

        var result = await this._sut.GetPokemon();

        var content = Assert.IsType<ContentResult>(result);
        Assert.Contains("Bulbasaur", content.Content);
        this._masterDataService.Verify(s => s.RefreshCacheAsync(), Times.Once);
    }

    [Fact]
    public async Task GetPokemonReturnsNotFoundWhenCacheMissAndRefreshFails()
    {
        this._masterDataService.Setup(s => s.GetPokemonDataAsync()).ReturnsAsync((string?)null);
        this._masterDataService.Setup(s => s.RefreshCacheAsync()).Returns(Task.CompletedTask);

        var result = await this._sut.GetPokemon();

        Assert.IsType<NotFoundObjectResult>(result);
    }

    // --- GetItems ---

    [Fact]
    public async Task GetItemsReturnsContentWhenCacheHit()
    {
        this._masterDataService.Setup(s => s.GetItemDataAsync()).ReturnsAsync(/*lang=json,strict*/ "{\"1\":\"Poke Ball\"}");
        var result = await this._sut.GetItems();
        Assert.IsType<ContentResult>(result);
    }

    [Fact]
    public async Task GetItemsRefreshesCacheWhenCacheMissThenReturnsContent()
    {
        var callCount = 0;
        this._masterDataService.Setup(s => s.GetItemDataAsync())
            .ReturnsAsync(() => ++callCount > 1 ? /*lang=json,strict*/ "{\"1\":\"Poke Ball\"}" : null);
        this._masterDataService.Setup(s => s.RefreshCacheAsync()).Returns(Task.CompletedTask);

        var result = await this._sut.GetItems();

        Assert.IsType<ContentResult>(result);
        this._masterDataService.Verify(s => s.RefreshCacheAsync(), Times.Once);
    }

    [Fact]
    public async Task GetItemsReturnsNotFoundWhenCacheMissAndRefreshFails()
    {
        this._masterDataService.Setup(s => s.GetItemDataAsync()).ReturnsAsync((string?)null);
        this._masterDataService.Setup(s => s.RefreshCacheAsync()).Returns(Task.CompletedTask);
        Assert.IsType<NotFoundObjectResult>(await this._sut.GetItems());
    }

    // --- GetGrunts ---

    [Fact]
    public async Task GetGruntsReturnsContentWhenAvailable()
    {
        this._poracleApiProxy.Setup(p => p.GetGruntsAsync()).ReturnsAsync(/*lang=json,strict*/ "{\"grunts\":[]}");
        var result = await this._sut.GetGrunts();
        Assert.IsType<ContentResult>(result);
    }

    [Fact]
    public async Task GetGruntsReturnsNotFoundWhenNull()
    {
        this._poracleApiProxy.Setup(p => p.GetGruntsAsync()).ReturnsAsync((string?)null);
        Assert.IsType<NotFoundObjectResult>(await this._sut.GetGrunts());
    }
}
