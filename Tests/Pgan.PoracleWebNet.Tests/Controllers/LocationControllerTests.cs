using Microsoft.AspNetCore.Mvc;
using Moq;
using Pgan.PoracleWebNet.Api.Controllers;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Tests.Controllers;

public class LocationControllerTests : ControllerTestBase
{
    private readonly Mock<IHumanService> _humanService = new();
    private readonly Mock<IProfileService> _profileService = new();
    private readonly Mock<IPoracleHumanProxy> _humanProxy = new();
    private readonly Mock<IPoracleApiProxy> _proxy = new();
    private readonly Mock<IHttpClientFactory> _httpClientFactory = new();
    private readonly Mock<IPlaceUpdateCapabilityService> _placeUpdateCapability = new();
    private readonly LocationController _sut;

    public LocationControllerTests()
    {
        this._sut = new LocationController(
            this._humanService.Object,
            this._profileService.Object,
            this._humanProxy.Object,
            this._proxy.Object,
            this._httpClientFactory.Object,
            this._placeUpdateCapability.Object);
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
    public async Task GetLocationFallsBackToHumanWhenProfileMissing()
    {
        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync("123456789", 1)).ReturnsAsync((Profile?)null);
        this._humanService.Setup(s => s.GetByIdAsync("123456789"))
            .ReturnsAsync(new Human { Id = "123456789", Latitude = 41.235, Longitude = -96.174 });

        var result = await this._sut.GetLocation();

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task GetLocationReturnsNotFoundWhenProfileAndHumanMissing()
    {
        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync("123456789", 1)).ReturnsAsync((Profile?)null);
        this._humanService.Setup(s => s.GetByIdAsync("123456789")).ReturnsAsync((Human?)null);

        Assert.IsType<NotFoundResult>(await this._sut.GetLocation());
    }

    // --- UpdateLocation ---

    [Fact]
    public async Task UpdateLocationCallsProxySetLocation()
    {
        var human = new Human { Id = "123456789", Latitude = 0, Longitude = 0 };
        this._humanService.Setup(s => s.GetByIdAsync("123456789")).ReturnsAsync(human);

        var result = await this._sut.UpdateLocation(
            new LocationController.LocationUpdateRequest { Latitude = 51.5074, Longitude = -0.1278 });

        Assert.IsType<OkObjectResult>(result);
        this._humanProxy.Verify(p => p.SetLocationAsync("123456789", 51.5074, -0.1278), Times.Once);
    }

    [Fact]
    public async Task UpdateLocationReturnsNotFoundWhenHumanMissing()
    {
        this._humanService.Setup(s => s.GetByIdAsync("123456789")).ReturnsAsync((Human?)null);

        Assert.IsType<NotFoundResult>(
            await this._sut.UpdateLocation(new LocationController.LocationUpdateRequest { Latitude = 0, Longitude = 0 }));
    }

    // --- Places ---

    [Fact]
    public async Task GetPlacesReportsWhetherThisServerCanMoveOne()
    {
        this._humanProxy.Setup(p => p.GetPlacesAsync("123456789")).ReturnsAsync(new SavedPlaces());
        this._placeUpdateCapability
            .Setup(c => c.IsPlaceUpdateAvailableAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var ok = Assert.IsType<OkObjectResult>(await this._sut.GetPlaces(CancellationToken.None));

        Assert.Equal(true, ok.Value?.GetType().GetProperty("canEdit")?.GetValue(ok.Value));
    }

    [Fact]
    public async Task UpdatePlaceMovesItAndAnswersTheNewList()
    {
        this._humanProxy
            .Setup(p => p.UpdatePlaceAsync("123456789", "home", 9.5, 8.5))
            .ReturnsAsync(true);
        this._humanProxy.Setup(p => p.GetPlacesAsync("123456789")).ReturnsAsync(new SavedPlaces());

        var result = await this._sut.UpdatePlace(
            "home", new LocationController.PlaceMoveRequest { Latitude = 9.5, Longitude = 8.5 });

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task UpdatePlaceSaysSoWhenTheServerCannotDoIt()
    {
        // No v1 equivalent exists, so there is nothing to fall back to. 501 rather than a 500, because
        // the request was fine and the server simply cannot.
        this._humanProxy
            .Setup(p => p.UpdatePlaceAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<double>(), It.IsAny<double>()))
            .ReturnsAsync(false);

        var result = await this._sut.UpdatePlace(
            "home", new LocationController.PlaceMoveRequest { Latitude = 9.5, Longitude = 8.5 });

        Assert.Equal(501, Assert.IsType<ObjectResult>(result).StatusCode);
    }

    [Theory]
    [InlineData(null, 8.5)]
    [InlineData(9.5, null)]
    [InlineData(91.0, 8.5)]
    [InlineData(9.5, 181.0)]
    public async Task UpdatePlaceRefusesCoordinatesThatAreNotOnEarth(double? latitude, double? longitude)
    {
        var result = await this._sut.UpdatePlace(
            "home", new LocationController.PlaceMoveRequest { Latitude = latitude, Longitude = longitude });

        Assert.IsType<BadRequestObjectResult>(result);
        this._humanProxy.Verify(
            p => p.UpdatePlaceAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<double>(), It.IsAny<double>()),
            Times.Never);
    }

    [Fact]
    public async Task UpdatePlaceAcceptsTheEdgesOfTheRange()
    {
        // The legitimate case beside the refusal: -90 and 180 are real places, and a guard written as
        // an exclusive range would have quietly refused the poles and the antimeridian.
        this._humanProxy.Setup(p => p.UpdatePlaceAsync("123456789", "home", -90, 180)).ReturnsAsync(true);
        this._humanProxy.Setup(p => p.GetPlacesAsync("123456789")).ReturnsAsync(new SavedPlaces());

        var result = await this._sut.UpdatePlace(
            "home", new LocationController.PlaceMoveRequest { Latitude = -90, Longitude = 180 });

        Assert.IsType<OkObjectResult>(result);
    }

    // --- UpdateLanguage ---

    [Fact]
    public async Task UpdateLanguageSetsLanguage()
    {
        var human = new Human { Id = "123456789", Language = "en" };
        this._humanService.Setup(s => s.GetByIdAsync("123456789")).ReturnsAsync(human);

        var result = await this.LanguageSut().UpdateLanguage(new NotificationLanguageController.LanguageUpdateRequest { Language = "de" });

        Assert.IsType<OkObjectResult>(result);
        this._humanService.Verify(s => s.SetLanguageAsync("123456789", "de"), Times.Once);
    }

    [Fact]
    public async Task UpdateLanguageAnswersWhatWasStoredRatherThanWhatWasSent()
    {
        // PoracleNG lowercases and trims: "pt-BR" is stored as "pt-br", on v1 and v2 alike. Echoing the
        // request would leave the SPA holding a value the server does not have, and its picker compares
        // the two strings.
        var human = new Human { Id = "123456789", Language = "en" };
        this._humanService.Setup(s => s.GetByIdAsync("123456789")).ReturnsAsync(human);
        this._humanService
            .Setup(s => s.SetLanguageAsync("123456789", "pt-BR"))
            .Callback(() => human.Language = "pt-br")
            .Returns(Task.CompletedTask);

        var result = await this.LanguageSut()
            .UpdateLanguage(new NotificationLanguageController.LanguageUpdateRequest { Language = "pt-BR" });

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal("pt-br", ok.Value?.GetType().GetProperty("language")?.GetValue(ok.Value));
    }

    [Fact]
    public async Task UpdateLanguageReturnsNotFoundWhenHumanMissing()
    {
        this._humanService.Setup(s => s.GetByIdAsync("123456789")).ReturnsAsync((Human?)null);
        Assert.IsType<NotFoundResult>(
            await this.LanguageSut().UpdateLanguage(new NotificationLanguageController.LanguageUpdateRequest { Language = "de" }));
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

    /// <summary>Language moved to its own controller so disable_location stops blocking it (#479).</summary>
    private NotificationLanguageController LanguageSut()
    {
        var c = new NotificationLanguageController(this._humanService.Object);
        SetupUser(c);
        return c;
    }
}
