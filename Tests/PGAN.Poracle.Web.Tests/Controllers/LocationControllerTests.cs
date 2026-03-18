using Microsoft.AspNetCore.Mvc;
using Moq;
using PGAN.Poracle.Web.Api.Controllers;
using PGAN.Poracle.Web.Core.Abstractions.Services;
using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Tests.Controllers;

public class LocationControllerTests : ControllerTestBase
{
    private readonly Mock<IHumanService> _humanService = new();
    private readonly Mock<IPoracleApiProxy> _proxy = new();
    private readonly Mock<IHttpClientFactory> _httpClientFactory = new();
    private readonly LocationController _sut;

    public LocationControllerTests()
    {
        _sut = new LocationController(_humanService.Object, _proxy.Object, _httpClientFactory.Object);
        SetupUser(_sut);
    }

    // --- GetLocation ---

    [Fact]
    public async Task GetLocation_ReturnsOk_WhenHumanFound()
    {
        _humanService.Setup(s => s.GetByIdAndProfileAsync("123456789", 1))
            .ReturnsAsync(new Human { Id = "123456789", Latitude = 40.7128, Longitude = -74.006 });

        var result = await _sut.GetLocation();

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task GetLocation_ReturnsNotFound_WhenHumanMissing()
    {
        _humanService.Setup(s => s.GetByIdAndProfileAsync("123456789", 1)).ReturnsAsync((Human?)null);
        Assert.IsType<NotFoundResult>(await _sut.GetLocation());
    }

    // --- UpdateLocation ---

    [Fact]
    public async Task UpdateLocation_UpdatesCoordinates()
    {
        var human = new Human { Id = "123456789", Latitude = 0, Longitude = 0 };
        _humanService.Setup(s => s.GetByIdAndProfileAsync("123456789", 1)).ReturnsAsync(human);
        _humanService.Setup(s => s.UpdateAsync(human)).ReturnsAsync(human);

        var result = await _sut.UpdateLocation(
            new LocationController.LocationUpdateRequest { Latitude = 51.5074, Longitude = -0.1278 });

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(51.5074, human.Latitude);
        Assert.Equal(-0.1278, human.Longitude);
    }

    [Fact]
    public async Task UpdateLocation_ReturnsNotFound_WhenHumanMissing()
    {
        _humanService.Setup(s => s.GetByIdAndProfileAsync("123456789", 1)).ReturnsAsync((Human?)null);
        Assert.IsType<NotFoundResult>(
            await _sut.UpdateLocation(new LocationController.LocationUpdateRequest { Latitude = 0, Longitude = 0 }));
    }

    // --- UpdateLanguage ---

    [Fact]
    public async Task UpdateLanguage_SetsLanguage()
    {
        var human = new Human { Id = "123456789", Language = "en" };
        _humanService.Setup(s => s.GetByIdAndProfileAsync("123456789", 1)).ReturnsAsync(human);
        _humanService.Setup(s => s.UpdateAsync(human)).ReturnsAsync(human);

        var result = await _sut.UpdateLanguage(new LocationController.LanguageUpdateRequest { Language = "de" });

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal("de", human.Language);
    }

    [Fact]
    public async Task UpdateLanguage_ReturnsNotFound_WhenHumanMissing()
    {
        _humanService.Setup(s => s.GetByIdAndProfileAsync("123456789", 1)).ReturnsAsync((Human?)null);
        Assert.IsType<NotFoundResult>(
            await _sut.UpdateLanguage(new LocationController.LanguageUpdateRequest { Language = "de" }));
    }

    // --- Geocode ---

    [Fact]
    public async Task Geocode_ReturnsBadRequest_WhenQueryEmpty()
    {
        var result = await _sut.Geocode("");
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Geocode_ReturnsBadRequest_WhenQueryWhitespace()
    {
        var result = await _sut.Geocode("   ");
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Geocode_ReturnsBadRequest_WhenNoProviderConfigured()
    {
        _proxy.Setup(p => p.GetConfigAsync()).ReturnsAsync(new PoracleConfig { ProviderUrl = "" });
        var result = await _sut.Geocode("London");
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Geocode_ReturnsBadRequest_WhenConfigNull()
    {
        _proxy.Setup(p => p.GetConfigAsync()).ReturnsAsync((PoracleConfig?)null);
        var result = await _sut.Geocode("London");
        Assert.IsType<BadRequestObjectResult>(result);
    }

    // --- GetStaticMap ---

    [Fact]
    public async Task GetStaticMap_ReturnsOk_WhenUrlAvailable()
    {
        _proxy.Setup(p => p.GetLocationMapUrlAsync(51.5, -0.1)).ReturnsAsync("https://map.example/img.png");
        var result = await _sut.GetStaticMap(51.5, -0.1);
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task GetStaticMap_ReturnsNotFound_WhenUrlNull()
    {
        _proxy.Setup(p => p.GetLocationMapUrlAsync(0, 0)).ReturnsAsync((string?)null);
        Assert.IsType<NotFoundResult>(await _sut.GetStaticMap(0, 0));
    }

    [Fact]
    public async Task GetStaticMap_ReturnsNotFound_WhenThrows()
    {
        _proxy.Setup(p => p.GetLocationMapUrlAsync(It.IsAny<double>(), It.IsAny<double>())).ThrowsAsync(new Exception());
        Assert.IsType<NotFoundResult>(await _sut.GetStaticMap(0, 0));
    }

    // --- GetDistanceMap ---

    [Fact]
    public async Task GetDistanceMap_ReturnsOk_WhenUrlAvailable()
    {
        _proxy.Setup(p => p.GetDistanceMapUrlAsync(51.5, -0.1, 500)).ReturnsAsync("https://map.example/dist.png");
        var result = await _sut.GetDistanceMap(51.5, -0.1, 500);
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task GetDistanceMap_ReturnsNotFound_WhenUrlNull()
    {
        _proxy.Setup(p => p.GetDistanceMapUrlAsync(0, 0, 0)).ReturnsAsync((string?)null);
        Assert.IsType<NotFoundResult>(await _sut.GetDistanceMap(0, 0, 0));
    }

    [Fact]
    public async Task GetDistanceMap_ReturnsNotFound_WhenThrows()
    {
        _proxy.Setup(p => p.GetDistanceMapUrlAsync(It.IsAny<double>(), It.IsAny<double>(), It.IsAny<int>()))
            .ThrowsAsync(new Exception());
        Assert.IsType<NotFoundResult>(await _sut.GetDistanceMap(0, 0, 0));
    }
}
