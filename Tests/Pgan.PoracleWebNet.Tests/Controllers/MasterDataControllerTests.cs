using Microsoft.AspNetCore.Mvc;
using Moq;
using Pgan.PoracleWebNet.Api.Controllers;
using Pgan.PoracleWebNet.Core.Abstractions.Services;

namespace Pgan.PoracleWebNet.Tests.Controllers;

public class MasterDataControllerTests : ControllerTestBase
{
    private readonly Mock<IMasterDataService> _masterDataService = new();
    private readonly Mock<IPoracleApiProxy> _poracleApiProxy = new();
    private readonly Mock<IRaidLevelService> _raidLevelService = new();
    private readonly MasterDataController _sut;

    public MasterDataControllerTests()
    {
        this._sut = new MasterDataController(
            this._masterDataService.Object,
            this._poracleApiProxy.Object,
            this._raidLevelService.Object);
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

    // --- GetMonsters ---

    [Fact]
    public async Task GetMonstersServesPoracleNgTranslationForTheRequestedLocale()
    {
        this._poracleApiProxy.Setup(p => p.GetMonstersAsync("de"))
            .ReturnsAsync(/*lang=json,strict*/ "{\"1_0\":{\"id\":1,\"name\":\"Bisasam\"}}");

        var result = await this._sut.GetMonsters("de");

        var content = Assert.IsType<ContentResult>(result);
        Assert.Contains("Bisasam", content.Content, StringComparison.Ordinal);
        this._masterDataService.Verify(s => s.GetMonsterDataAsync(), Times.Never);
    }

    [Fact]
    public async Task GetMonstersDefaultsToEnglishWhenNoLocaleIsGiven()
    {
        this._poracleApiProxy.Setup(p => p.GetMonstersAsync("en"))
            .ReturnsAsync(/*lang=json,strict*/ "{\"1_0\":{\"id\":1,\"name\":\"Bulbasaur\"}}");

        Assert.IsType<ContentResult>(await this._sut.GetMonsters(null));

        this._poracleApiProxy.Verify(p => p.GetMonstersAsync("en"), Times.Once);
    }

    /// <summary>
    /// PoracleJS and older PoracleNG builds do not serve /api/masterdata/monsters. Falling back to
    /// the English masterfile keeps names, types and forms in the selector instead of emptying it.
    /// </summary>
    [Fact]
    public async Task GetMonstersFallsBackToTheEnglishMasterfileWhenUpstreamHasNoSuchRoute()
    {
        this._poracleApiProxy.Setup(p => p.GetMonstersAsync(It.IsAny<string>())).ReturnsAsync((string?)null);
        this._masterDataService.Setup(s => s.GetMonsterDataAsync())
            .ReturnsAsync(/*lang=json,strict*/ "{\"1_0\":{\"id\":1,\"name\":\"Bulbasaur\"}}");

        var content = Assert.IsType<ContentResult>(await this._sut.GetMonsters("de"));

        Assert.Contains("Bulbasaur", content.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetMonstersFallsBackWhenUpstreamIsUnreachable()
    {
        this._poracleApiProxy.Setup(p => p.GetMonstersAsync(It.IsAny<string>())).ThrowsAsync(new HttpRequestException("down"));
        this._masterDataService.Setup(s => s.GetMonsterDataAsync())
            .ReturnsAsync(/*lang=json,strict*/ "{\"1_0\":{\"id\":1,\"name\":\"Bulbasaur\"}}");

        var content = Assert.IsType<ContentResult>(await this._sut.GetMonsters("de"));

        Assert.Contains("Bulbasaur", content.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetMonstersReturnsNotFoundWhenNeitherSourceHasData()
    {
        this._poracleApiProxy.Setup(p => p.GetMonstersAsync(It.IsAny<string>())).ReturnsAsync((string?)null);
        this._masterDataService.Setup(s => s.GetMonsterDataAsync()).ReturnsAsync((string?)null);
        this._masterDataService.Setup(s => s.RefreshCacheAsync()).Returns(Task.CompletedTask);

        Assert.IsType<NotFoundObjectResult>(await this._sut.GetMonsters("en"));

        this._masterDataService.Verify(s => s.RefreshCacheAsync(), Times.Once);
    }

    [Theory]
    // Every locale the UI can be set to has to survive normalization - refusing one would silently
    // send that user back to English names.
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("fr")]
    [InlineData("es")]
    [InlineData("nl")]
    [InlineData("it")]
    [InlineData("pt")]
    [InlineData("pt-BR")]
    [InlineData("pl")]
    [InlineData("da")]
    [InlineData("sv")]
    // PoracleNG's own locale codes, which an admin can set as the Poracle default.
    [InlineData("ja")]
    [InlineData("ru")]
    [InlineData("zh-cn")]
    [InlineData("nb-no")]
    public void NormalizeLocaleKeepsEverySupportedLocale(string locale)
    {
        Assert.Equal(locale, MasterDataController.NormalizeLocale(locale));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("de&foo=bar")]
    [InlineData("../../config")]
    [InlineData("e")]
    public void NormalizeLocaleFallsBackToEnglishForAnythingElse(string? locale)
    {
        Assert.Equal("en", MasterDataController.NormalizeLocale(locale));
    }
}
