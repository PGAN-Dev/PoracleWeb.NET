using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using PGAN.Poracle.Web.Api.Controllers;
using PGAN.Poracle.Web.Core.Abstractions.Services;
using PGAN.Poracle.Web.Core.Models;
using PGAN.Poracle.Web.Data;

namespace PGAN.Poracle.Web.Tests.Controllers;

public class LocationControllerTests : ControllerTestBase, IDisposable
{
    private readonly Mock<IHumanService> _humanService = new();
    private readonly Mock<IProfileService> _profileService = new();
    private readonly Mock<IPoracleApiProxy> _proxy = new();
    private readonly Mock<IHttpClientFactory> _httpClientFactory = new();
    private readonly PoracleContext _dbContext;
    private readonly LocationController _sut;

    public LocationControllerTests()
    {
        var options = new DbContextOptionsBuilder<PoracleContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        this._dbContext = new PoracleContext(options);
        this._sut = new LocationController(this._humanService.Object, this._profileService.Object, this._proxy.Object, this._httpClientFactory.Object, this._dbContext);
        SetupUser(this._sut);
    }

    // --- GetLocation ---

    [Fact]
    public async Task GetLocationReturnsOkWhenProfileFound()
    {
        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync("123456789", 1))
            .ReturnsAsync(new Profile { Id = "123456789", ProfileNo = 1, Latitude = 40.7128, Longitude = -74.006 });

        var result = await this._sut.GetLocation();

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task GetLocationReturnsNotFoundWhenProfileMissing()
    {
        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync("123456789", 1)).ReturnsAsync((Profile?)null);
        Assert.IsType<NotFoundResult>(await this._sut.GetLocation());
    }

    // --- UpdateLocation ---

    [Fact]
    public async Task UpdateLocationUpdatesCoordinates()
    {
        var profile = new Profile { Id = "123456789", ProfileNo = 1, Latitude = 0, Longitude = 0 };
        var human = new Human { Id = "123456789", Latitude = 0, Longitude = 0 };
        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync("123456789", 1)).ReturnsAsync(profile);
        this._profileService.Setup(s => s.UpdateAsync(profile)).ReturnsAsync(profile);
        this._humanService.Setup(s => s.GetByIdAsync("123456789")).ReturnsAsync(human);
        this._humanService.Setup(s => s.UpdateAsync(human)).ReturnsAsync(human);

        var result = await this._sut.UpdateLocation(
            new LocationController.LocationUpdateRequest { Latitude = 51.5074, Longitude = -0.1278 });

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(51.5074, profile.Latitude);
        Assert.Equal(-0.1278, profile.Longitude);
        Assert.Equal(51.5074, human.Latitude);
        Assert.Equal(-0.1278, human.Longitude);
    }

    [Fact]
    public async Task UpdateLocationReturnsNotFoundWhenProfileMissing()
    {
        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync("123456789", 1)).ReturnsAsync((Profile?)null);
        Assert.IsType<NotFoundResult>(
            await this._sut.UpdateLocation(new LocationController.LocationUpdateRequest { Latitude = 0, Longitude = 0 }));
    }

    // --- UpdateLanguage ---

    [Fact]
    public async Task UpdateLanguageSetsLanguage()
    {
        var human = new Human { Id = "123456789", Language = "en" };
        this._humanService.Setup(s => s.GetByIdAndProfileAsync("123456789", 1)).ReturnsAsync(human);
        this._humanService.Setup(s => s.UpdateAsync(human)).ReturnsAsync(human);

        var result = await this._sut.UpdateLanguage(new LocationController.LanguageUpdateRequest { Language = "de" });

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal("de", human.Language);
    }

    [Fact]
    public async Task UpdateLanguageReturnsNotFoundWhenHumanMissing()
    {
        this._humanService.Setup(s => s.GetByIdAndProfileAsync("123456789", 1)).ReturnsAsync((Human?)null);
        Assert.IsType<NotFoundResult>(
            await this._sut.UpdateLanguage(new LocationController.LanguageUpdateRequest { Language = "de" }));
    }

    // --- Geocode ---

    [Fact]
    public async Task GeocodeReturnsBadRequestWhenQueryEmpty()
    {
        var result = await this._sut.Geocode("");
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task GeocodeReturnsBadRequestWhenQueryWhitespace()
    {
        var result = await this._sut.Geocode("   ");
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task GeocodeReturnsBadRequestWhenNoProviderConfigured()
    {
        this._proxy.Setup(p => p.GetConfigAsync()).ReturnsAsync(new PoracleConfig { ProviderUrl = "" });
        var result = await this._sut.Geocode("London");
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task GeocodeReturnsBadRequestWhenConfigNull()
    {
        this._proxy.Setup(p => p.GetConfigAsync()).ReturnsAsync((PoracleConfig?)null);
        var result = await this._sut.Geocode("London");
        Assert.IsType<BadRequestObjectResult>(result);
    }

    // --- GetStaticMap ---

    [Fact]
    public async Task GetStaticMapReturnsOkWhenUrlAvailable()
    {
        this._proxy.Setup(p => p.GetLocationMapUrlAsync(51.5, -0.1)).ReturnsAsync("https://map.example/img.png");
        var result = await this._sut.GetStaticMap(51.5, -0.1);
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task GetStaticMapReturnsNotFoundWhenUrlNull()
    {
        this._proxy.Setup(p => p.GetLocationMapUrlAsync(0, 0)).ReturnsAsync((string?)null);
        Assert.IsType<NotFoundResult>(await this._sut.GetStaticMap(0, 0));
    }

    [Fact]
    public async Task GetStaticMapReturnsNotFoundWhenThrows()
    {
        this._proxy.Setup(p => p.GetLocationMapUrlAsync(It.IsAny<double>(), It.IsAny<double>())).ThrowsAsync(new InvalidOperationException());
        Assert.IsType<NotFoundResult>(await this._sut.GetStaticMap(0, 0));
    }

    // --- GetDistanceMap ---

    [Fact]
    public async Task GetDistanceMapReturnsOkWhenUrlAvailable()
    {
        this._proxy.Setup(p => p.GetDistanceMapUrlAsync(51.5, -0.1, 500)).ReturnsAsync("https://map.example/dist.png");
        var result = await this._sut.GetDistanceMap(51.5, -0.1, 500);
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task GetDistanceMapReturnsNotFoundWhenUrlNull()
    {
        this._proxy.Setup(p => p.GetDistanceMapUrlAsync(0, 0, 0)).ReturnsAsync((string?)null);
        Assert.IsType<NotFoundResult>(await this._sut.GetDistanceMap(0, 0, 0));
    }

    [Fact]
    public async Task GetDistanceMapReturnsNotFoundWhenThrows()
    {
        this._proxy.Setup(p => p.GetDistanceMapUrlAsync(It.IsAny<double>(), It.IsAny<double>(), It.IsAny<int>()))
            .ThrowsAsync(new InvalidOperationException());
        Assert.IsType<NotFoundResult>(await this._sut.GetDistanceMap(0, 0, 0));
    }

    public void Dispose() => GC.SuppressFinalize(this);
}
