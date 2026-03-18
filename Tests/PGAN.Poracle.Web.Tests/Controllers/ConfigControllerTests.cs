using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using PGAN.Poracle.Web.Api.Controllers;
using PGAN.Poracle.Web.Core.Abstractions.Services;
using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Tests.Controllers;

public class ConfigControllerTests : ControllerTestBase
{
    private readonly Mock<IPoracleApiProxy> _proxy = new();
    private readonly Mock<ILogger<ConfigController>> _logger = new();
    private readonly ConfigController _sut;

    public ConfigControllerTests()
    {
        _sut = new ConfigController(_proxy.Object, _logger.Object);
        SetupUser(_sut);
    }

    // --- GetTemplates ---

    [Fact]
    public async Task GetTemplates_ReturnsContent_WhenAvailable()
    {
        _proxy.Setup(p => p.GetTemplatesAsync()).ReturnsAsync("{\"templates\":{}}");

        var result = await _sut.GetTemplates();

        var content = Assert.IsType<ContentResult>(result);
        Assert.Equal("application/json", content.ContentType);
    }

    [Fact]
    public async Task GetTemplates_ReturnsNull_BranchReturnsFallback()
    {
        _proxy.Setup(p => p.GetTemplatesAsync()).ReturnsAsync((string?)null);

        var result = await _sut.GetTemplates();

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task GetTemplates_ReturnsOkFallback_WhenExceptionThrown()
    {
        _proxy.Setup(p => p.GetTemplatesAsync()).ThrowsAsync(new HttpRequestException("fail"));

        var result = await _sut.GetTemplates();

        Assert.IsType<OkObjectResult>(result);
    }

    // --- GetConfig ---

    [Fact]
    public async Task GetConfig_ReturnsOk_WhenAvailable()
    {
        var config = new PoracleConfig { Locale = "en", MaxDistance = 5000 };
        _proxy.Setup(p => p.GetConfigAsync()).ReturnsAsync(config);

        var result = await _sut.GetConfig();

        var ok = Assert.IsType<OkObjectResult>(result);
        var returned = Assert.IsType<PoracleConfig>(ok.Value);
        Assert.Equal("en", returned.Locale);
        Assert.Equal(5000, returned.MaxDistance);
    }

    [Fact]
    public async Task GetConfig_ReturnsFallbackConfig_WhenNull()
    {
        _proxy.Setup(p => p.GetConfigAsync()).ReturnsAsync((PoracleConfig?)null);

        var result = await _sut.GetConfig();

        var ok = Assert.IsType<OkObjectResult>(result);
        var config = Assert.IsType<PoracleConfig>(ok.Value);
        Assert.Equal("en", config.Locale);
        Assert.Equal("unknown", config.PoracleVersion);
        Assert.Equal(10726000, config.MaxDistance);
    }

    [Fact]
    public async Task GetConfig_ReturnsFallbackConfig_WhenExceptionThrown()
    {
        _proxy.Setup(p => p.GetConfigAsync()).ThrowsAsync(new Exception("fail"));

        var result = await _sut.GetConfig();

        var ok = Assert.IsType<OkObjectResult>(result);
        var config = Assert.IsType<PoracleConfig>(ok.Value);
        Assert.Equal("en", config.Locale);
    }

    // --- GetDts ---

    [Fact]
    public void GetDts_ReturnsOkEmptyArray_WhenCacheEmpty()
    {
        // DtsCacheService.GetCachedDts() returns null/empty when not configured
        var result = _sut.GetDts();
        Assert.IsType<OkObjectResult>(result);
    }
}
