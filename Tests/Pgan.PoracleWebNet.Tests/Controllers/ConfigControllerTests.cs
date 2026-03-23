using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Pgan.PoracleWebNet.Api.Controllers;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Tests.Controllers;

public class ConfigControllerTests : ControllerTestBase
{
    private readonly Mock<IPoracleApiProxy> _proxy = new();
    private readonly Mock<ILogger<ConfigController>> _logger = new();
    private readonly ConfigController _sut;

    public ConfigControllerTests()
    {
        this._sut = new ConfigController(this._proxy.Object, this._logger.Object);
        SetupUser(this._sut);
    }

    // --- GetTemplates ---

    [Fact]
    public async Task GetTemplatesReturnsContentWhenAvailable()
    {
        this._proxy.Setup(p => p.GetTemplatesAsync()).ReturnsAsync(/*lang=json,strict*/ "{\"templates\":{}}");

        var result = await this._sut.GetTemplates();

        var content = Assert.IsType<ContentResult>(result);
        Assert.Equal("application/json", content.ContentType);
    }

    [Fact]
    public async Task GetTemplatesReturnsNullBranchReturnsFallback()
    {
        this._proxy.Setup(p => p.GetTemplatesAsync()).ReturnsAsync((string?)null);

        var result = await this._sut.GetTemplates();

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task GetTemplatesReturnsOkFallbackWhenExceptionThrown()
    {
        this._proxy.Setup(p => p.GetTemplatesAsync()).ThrowsAsync(new HttpRequestException("fail"));

        var result = await this._sut.GetTemplates();

        Assert.IsType<OkObjectResult>(result);
    }

    // --- GetConfig ---

    [Fact]
    public async Task GetConfigReturnsOkWhenAvailable()
    {
        var config = new PoracleConfig { Locale = "en", MaxDistance = 5000 };
        this._proxy.Setup(p => p.GetConfigAsync()).ReturnsAsync(config);

        var result = await this._sut.GetConfig();

        var ok = Assert.IsType<OkObjectResult>(result);
        var returned = Assert.IsType<PoracleConfig>(ok.Value);
        Assert.Equal("en", returned.Locale);
        Assert.Equal(5000, returned.MaxDistance);
    }

    [Fact]
    public async Task GetConfigReturnsFallbackConfigWhenNull()
    {
        this._proxy.Setup(p => p.GetConfigAsync()).ReturnsAsync((PoracleConfig?)null);

        var result = await this._sut.GetConfig();

        var ok = Assert.IsType<OkObjectResult>(result);
        var config = Assert.IsType<PoracleConfig>(ok.Value);
        Assert.Equal("en", config.Locale);
        Assert.Equal("unknown", config.PoracleVersion);
        Assert.Equal(10726000, config.MaxDistance);
    }

    [Fact]
    public async Task GetConfigReturnsFallbackConfigWhenExceptionThrown()
    {
        this._proxy.Setup(p => p.GetConfigAsync()).ThrowsAsync(new InvalidOperationException("fail"));

        var result = await this._sut.GetConfig();

        var ok = Assert.IsType<OkObjectResult>(result);
        var config = Assert.IsType<PoracleConfig>(ok.Value);
        Assert.Equal("en", config.Locale);
    }

    // --- GetDts ---

    [Fact]
    public void GetDtsReturnsOkEmptyArrayWhenCacheEmpty()
    {
        // DtsCacheService.GetCachedDts() returns null/empty when not configured
        var result = this._sut.GetDts();
        Assert.IsType<OkObjectResult>(result);
    }
}
