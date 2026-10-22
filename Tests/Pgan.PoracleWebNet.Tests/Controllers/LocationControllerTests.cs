using Microsoft.AspNetCore.Mvc;
using Moq;
using Pgan.PoracleWebNet.Api.Controllers;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Models.Helpers;

namespace Pgan.PoracleWebNet.Tests.Controllers;

public class LocationControllerTests : ControllerTestBase
{
    private readonly Mock<IHumanService> _humanService = new();
    private readonly Mock<IProfileService> _profileService = new();
    private readonly Mock<IPoracleHumanProxy> _humanProxy = new();
    private readonly Mock<IPoracleApiProxy> _proxy = new();
    private readonly LocationController _sut;

    public LocationControllerTests()
    {
        this._sut = new LocationController(
            this._humanService.Object,
            this._profileService.Object,
            this._humanProxy.Object,
            this._proxy.Object);
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
    public async Task GetPlacesCarriesNoCapabilityFlag()
    {
        this._humanProxy.Setup(p => p.GetPlacesAsync("123456789")).ReturnsAsync(new SavedPlaces());

        var ok = Assert.IsType<OkObjectResult>(await this._sut.GetPlaces());

        Assert.Null(ok.Value?.GetType().GetProperty("canEdit"));
        Assert.NotNull(ok.Value?.GetType().GetProperty("Named"));
    }

    [Fact]
    public async Task AddPlaceAnswersTheSameShapeAsTheList()
    {
        // The SPA replaces its whole places signal from this reply. A body without canEdit cleared
        // the flag and took the edit control off every card until the next reload -- the exact
        // hazard the PUT already guards against, on the sibling path.
        this._humanProxy
            .Setup(p => p.AddPlaceAsync("123456789", It.IsAny<SavedPlace>()))
            .ReturnsAsync((string?)null);
        this._humanProxy.Setup(p => p.GetPlacesAsync("123456789")).ReturnsAsync(new SavedPlaces());

        var ok = Assert.IsType<OkObjectResult>(
            await this._sut.AddPlace(new SavedPlace { Label = "work", Latitude = 1, Longitude = 2 }));

        Assert.Null(ok.Value?.GetType().GetProperty("canEdit"));
    }

    [Theory]
    [InlineData("Gym\u202Eevil")]
    [InlineData("Line\nbreak")]
    [InlineData("   ")]
    [InlineData("")]
    public async Task AddPlaceRefusesALabelThePlaceRuleRefusesWithItsReason(string label)
    {
        var result = await this._sut.AddPlace(new SavedPlace { Label = label, Latitude = 1, Longitude = 2 });

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(PlaceLabelRules.Validate(label), bad.Value?.GetType().GetProperty("error")?.GetValue(bad.Value));
        this._humanProxy.Verify(p => p.AddPlaceAsync(It.IsAny<string>(), It.IsAny<SavedPlace>()), Times.Never);
    }

    [Fact]
    public async Task AddPlaceTrimsTheLabelItSaves()
    {
        this._humanProxy
            .Setup(p => p.AddPlaceAsync("123456789", It.IsAny<SavedPlace>()))
            .ReturnsAsync((string?)null);
        this._humanProxy.Setup(p => p.GetPlacesAsync("123456789")).ReturnsAsync(new SavedPlaces());

        Assert.IsType<OkObjectResult>(
            await this._sut.AddPlace(new SavedPlace { Label = "  Parents' house  ", Latitude = 1, Longitude = 2 }));

        this._humanProxy.Verify(p => p.AddPlaceAsync("123456789", It.Is<SavedPlace>(s => s.Label == "Parents' house")), Times.Once);
    }

    /// <summary>
    /// The rule judges new labels only. A label stored before it (the audit left "Gym" + U+202E + "evil"
    /// on a test account) must still be movable and deletable, or the user could never get rid of it.
    /// </summary>
    [Fact]
    public async Task ALabelStoredBeforeTheRuleCanStillBeMovedAndDeleted()
    {
        const string legacy = "Gym\u202Eevil";
        this._humanProxy.Setup(p => p.UpdatePlaceAsync("123456789", legacy, 1, 2)).Returns(Task.CompletedTask);
        this._humanProxy.Setup(p => p.DeletePlaceAsync("123456789", legacy)).Returns(Task.CompletedTask);
        this._humanProxy.Setup(p => p.GetPlacesAsync("123456789")).ReturnsAsync(new SavedPlaces());

        Assert.IsType<OkObjectResult>(
            await this._sut.UpdatePlace(legacy, new LocationController.PlaceMoveRequest { Latitude = 1, Longitude = 2 }));
        Assert.IsType<NoContentResult>(await this._sut.DeletePlace(legacy));
    }

    // --- PinUsage ---

    [Fact]
    public async Task PinUsageCountsRadiusAlarmsThatMeasureFromThePin()
    {
        // Counted: a radius with no place. Not counted: a radius from a saved place (it keeps its anchor),
        // a rule limited to areas, and a rule with no radius. Every type in the snapshot is read.
        var snapshot = System.Text.Json.JsonDocument.Parse("""
            {
              "pokemon": [
                { "uid": 1, "distance": 500, "override_location_label": "" },
                { "uid": 2, "distance": 500, "override_location_label": "Work" },
                { "uid": 3, "distance": 0, "override_areas": ["downtown"] },
                { "uid": 4, "distance": 0 }
              ],
              "raid": [ { "uid": 5, "distance": 1000 } ],
              "profile": { "name": "not an array" }
            }
            """).RootElement;
        var tracking = new Mock<IPoracleTrackingProxy>();
        tracking.Setup(t => t.GetAllTrackingAsync("123456789")).ReturnsAsync(snapshot);

        var ok = Assert.IsType<OkObjectResult>(await this._sut.GetPinUsage(tracking.Object));

        Assert.Equal(2, ok.Value?.GetType().GetProperty("alarms")?.GetValue(ok.Value));
    }

    /// <summary>
    /// The place-delete pre-check (flows D6): the confirm lists what is in the way before it offers Delete.
    /// Counted on the active profile across every type, a Pokéstop event folded into invasion included, and
    /// matched on the exact label, so a rule aimed at another place, or at the pin, is not in the list.
    /// </summary>
    [Fact]
    public async Task PlaceUsageListsTheRulesAimedAtThePlaceWithTheirSummaries()
    {
        var snapshot = System.Text.Json.JsonDocument.Parse("""
            {
              "pokemon": [
                { "uid": 37337, "distance": 500, "override_location_label": "Work", "description": "Pikachu 90%+" },
                { "uid": 2, "distance": 500, "override_location_label": "Home" },
                { "uid": 3, "distance": 500, "override_location_label": "" }
              ],
              "raid": [ { "uid": 9, "distance": 1000, "override_location_label": "Work" } ],
              "invasion": [
                { "uid": 11, "distance": 800, "grunt_type": "kecleon", "override_location_label": "Work" },
                { "uid": 12, "distance": 800, "grunt_type": "water", "override_location_label": "work" }
              ]
            }
            """).RootElement;
        var tracking = new Mock<IPoracleTrackingProxy>();
        tracking.Setup(t => t.GetAllTrackingWithDescriptionsAsync("123456789")).ReturnsAsync(snapshot);

        var ok = Assert.IsType<OkObjectResult>(await this._sut.GetPlaceUsage("Work", tracking.Object));

        Assert.Equal(3, ok.Value?.GetType().GetProperty("alarms")?.GetValue(ok.Value));
        var rules = Assert.IsAssignableFrom<IReadOnlyList<PlaceUsageRule>>(ok.Value?.GetType().GetProperty("rules")?.GetValue(ok.Value));
        Assert.Equal(
            [new PlaceUsageRule("pokemon", 37337, "Pikachu 90%+"), new PlaceUsageRule("raid", 9, null), new PlaceUsageRule("incident", 11, null)],
            rules);
    }

    [Fact]
    public async Task PlaceUsageAnswersNoneForAPlaceNothingUses()
    {
        var snapshot = System.Text.Json.JsonDocument.Parse("""{ "pokemon": [ { "uid": 1, "distance": 500 } ] }""").RootElement;
        var tracking = new Mock<IPoracleTrackingProxy>();
        tracking.Setup(t => t.GetAllTrackingWithDescriptionsAsync("123456789")).ReturnsAsync(snapshot);

        var ok = Assert.IsType<OkObjectResult>(await this._sut.GetPlaceUsage("Work", tracking.Object));

        Assert.Equal(0, ok.Value?.GetType().GetProperty("alarms")?.GetValue(ok.Value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public async Task PlaceUsageRefusesAMissingLabelWithoutReadingAnything(string? label)
    {
        var tracking = new Mock<IPoracleTrackingProxy>();

        Assert.IsType<BadRequestObjectResult>(await this._sut.GetPlaceUsage(label, tracking.Object));
        tracking.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UpdatePlaceMovesItAndAnswersTheNewList()
    {
        this._humanProxy
            .Setup(p => p.UpdatePlaceAsync("123456789", "home", 9.5, 8.5))
            .Returns(Task.CompletedTask);
        this._humanProxy.Setup(p => p.GetPlacesAsync("123456789")).ReturnsAsync(new SavedPlaces());

        var result = await this._sut.UpdatePlace(
            "home", new LocationController.PlaceMoveRequest { Latitude = 9.5, Longitude = 8.5 });

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task UpdatePlaceLetsAnUnsupportedServerReachTheExceptionFilter()
    {
        // No 501 from the controller any more: the proxy throws and PoracleUnsupportedExceptionFilter maps it.
        this._humanProxy
            .Setup(p => p.UpdatePlaceAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<double>(), It.IsAny<double>()))
            .ThrowsAsync(new PoracleUnsupportedException("moving a saved place", "PoracleNG 5.4.0"));

        await Assert.ThrowsAsync<PoracleUnsupportedException>(() => this._sut.UpdatePlace(
            "home", new LocationController.PlaceMoveRequest { Latitude = 9.5, Longitude = 8.5 }));
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
        this._humanProxy.Setup(p => p.UpdatePlaceAsync("123456789", "home", -90, 180)).Returns(Task.CompletedTask);
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
    public async Task UpdateLanguageThatLandedIsNotReportedAsAFailureWhenTheReadBackFails()
    {
        // The language is stored before the read-back runs. That read throws on a failed answer since
        // #994; answering 500 here would tell the user a change that took effect did not. The request is
        // the fallback it always was.
        this._humanService.SetupSequence(s => s.GetByIdAsync("123456789"))
            .ReturnsAsync(new Human { Id = "123456789", Language = "en" })
            .ThrowsAsync(new HttpRequestException("503", null, System.Net.HttpStatusCode.ServiceUnavailable));

        var result = await this.LanguageSut()
            .UpdateLanguage(new NotificationLanguageController.LanguageUpdateRequest { Language = "de" });

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal("de", ok.Value?.GetType().GetProperty("language")?.GetValue(ok.Value));
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
    public async Task GeocodeReturnsServiceUnavailableWhenPoracleAnswersNull()
    {
        this._proxy.Setup(p => p.GetGeocodeForwardAsync("London", null)).ReturnsAsync((string?)null);

        var result = await this._sut.Geocode("London");

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(503, objectResult.StatusCode);
    }

    [Fact]
    public async Task GeocodeReturnsPoracleResultVerbatim()
    {
        const string body = /*lang=json,strict*/ """[{"latitude":41.65,"longitude":-83.53,"displayName":"Toledo"}]""";
        this._proxy.Setup(p => p.GetGeocodeForwardAsync("Toledo", null)).ReturnsAsync(body);

        var result = await this._sut.Geocode("Toledo");

        var content = Assert.IsType<ContentResult>(result);
        Assert.Equal(body, content.Content);
        Assert.Equal("application/json", content.ContentType);
    }

    [Fact]
    public async Task GeocodePassesLanguageThrough()
    {
        this._proxy.Setup(p => p.GetGeocodeForwardAsync("Toledo", "de")).ReturnsAsync("[]");

        await this._sut.Geocode("Toledo", "de");

        this._proxy.Verify(p => p.GetGeocodeForwardAsync("Toledo", "de"), Times.Once);
    }

    // --- ReverseGeocode ---

    [Fact]
    public async Task ReverseGeocodeReturnsServiceUnavailableWhenPoracleAnswersNull()
    {
        this._proxy.Setup(p => p.GetGeocodeReverseAsync(41.65, -83.53, null)).ReturnsAsync((string?)null);

        var result = await this._sut.ReverseGeocode(41.65, -83.53);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(503, objectResult.StatusCode);
    }

    [Fact]
    public async Task ReverseGeocodeReturnsPoracleResultVerbatim()
    {
        const string body = /*lang=json,strict*/ """{"displayName":"Toledo, Ohio","latitude":41.65,"longitude":-83.53}""";
        this._proxy.Setup(p => p.GetGeocodeReverseAsync(41.65, -83.53, null)).ReturnsAsync(body);

        var result = await this._sut.ReverseGeocode(41.65, -83.53);

        var content = Assert.IsType<ContentResult>(result);
        Assert.Equal(body, content.Content);
        Assert.Equal("application/json", content.ContentType);
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
