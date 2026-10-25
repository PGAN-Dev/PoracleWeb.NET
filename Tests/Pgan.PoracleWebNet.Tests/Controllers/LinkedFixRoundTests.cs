using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Pgan.PoracleWebNet.Api.Configuration;
using Pgan.PoracleWebNet.Api.Controllers;
using Pgan.PoracleWebNet.Api.Services;
using Pgan.PoracleWebNet.Api.Services.Oidc;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Tests.Helpers;

namespace Pgan.PoracleWebNet.Tests.Controllers;

/// <summary>
/// The pair-warning header contract, the alerts toggle, drifted profiles, and linked profile import.
/// </summary>
public class LinkedFixRoundTests : ControllerTestBase
{
    private const string Discord = PairTestSupport.DiscordId;
    private const string Telegram = PairTestSupport.TelegramId;

    private readonly Mock<IPoracleHumanProxy> _humanProxy = new();
    private readonly Mock<IHumanService> _humanService = new();
    private readonly Mock<IProfileService> _profileService = new();

    private static string? Header(ControllerBase c) =>
        c.Response.Headers.TryGetValue(PairResponse.HeaderName, out var v) ? v.ToString() : null;

    private static string Body(IActionResult r) => JsonSerializer.Serialize(((ObjectResult)r).Value);

    // ---- header contract

    private LocationController Location(Mock<ILinkedAccount> linked)
    {
        var sut = new LocationController(
            this._humanService.Object, this._profileService.Object, this._humanProxy.Object,
            Mock.Of<IPoracleApiProxy>(), PairTestSupport.Writer(linked));
        SetupUser(sut, Discord);
        this._humanService.Setup(h => h.GetByIdAsync(Discord)).ReturnsAsync(new Human { Id = Discord });
        return sut;
    }

    [Fact]
    public async Task WarnedResponse_BodyAndStatusEqualTheUnwarnedOne_OnlyTheHeaderDiffers()
    {
        var request = new LocationController.LocationUpdateRequest { Latitude = 51.5, Longitude = -0.12 };
        var clean = this.Location(PairTestSupport.Linked(PairTestSupport.Pair(Discord)));
        var cleanResult = await clean.UpdateLocation(request);

        this._humanProxy.Setup(p => p.SetLocationAsync(Telegram, It.IsAny<double>(), It.IsAny<double>()))
            .ThrowsAsync(new HttpRequestException("connect ECONNREFUSED 192.0.2.10:3030"));
        var warned = this.Location(PairTestSupport.Linked(PairTestSupport.Pair(Discord)));
        var warnedResult = await warned.UpdateLocation(request);

        Assert.Null(Header(clean));
        Assert.Equal(Body(cleanResult), Body(warnedResult));
        Assert.Equal(((ObjectResult)cleanResult).StatusCode ?? 200, ((ObjectResult)warnedResult).StatusCode ?? 200);
        var header = JsonDocument.Parse(Header(warned)!).RootElement;
        Assert.Equal("telegram", header.GetProperty("side").GetString());
    }

    [Fact]
    public async Task Header_NeverCarriesTheHostOrPortOfAnHttpFailure()
    {
        this._humanProxy.Setup(p => p.SetLocationAsync(Telegram, It.IsAny<double>(), It.IsAny<double>()))
            .ThrowsAsync(new HttpRequestException("No connection could be made to 192.0.2.10:3030"));
        var sut = this.Location(PairTestSupport.Linked(PairTestSupport.Pair(Discord)));

        await sut.UpdateLocation(new LocationController.LocationUpdateRequest { Latitude = 1, Longitude = 2 });

        var header = Header(sut)!;
        Assert.DoesNotContain("192.0.2.10", header);
        Assert.DoesNotContain("3030", header);
        Assert.Equal("Poracle couldn't be reached.", JsonDocument.Parse(header).RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Header_PassesOnPoracleNgsRefusalDetail()
    {
        this._humanProxy.Setup(p => p.SetLocationAsync(Telegram, It.IsAny<double>(), It.IsAny<double>()))
            .ThrowsAsync(new PoracleRequestRefusedException("latitude out of range"));
        var sut = this.Location(PairTestSupport.Linked(PairTestSupport.Pair(Discord)));

        await sut.UpdateLocation(new LocationController.LocationUpdateRequest { Latitude = 1, Longitude = 2 });

        Assert.Contains("latitude out of range", Header(sut));
    }

    [Fact]
    public async Task DeletePlace_Warned_IsStillA204WithTheHeader()
    {
        this._humanProxy.Setup(p => p.DeletePlaceAsync(Telegram, "Home")).ThrowsAsync(new HttpRequestException("x"));
        var sut = this.Location(PairTestSupport.Linked(PairTestSupport.Pair(Discord)));

        var result = await sut.DeletePlace("Home");

        Assert.IsType<NoContentResult>(result);
        Assert.NotNull(Header(sut));
    }

    // ---- alerts toggle never restarts a paused side

    private AuthController Auth(Mock<ILinkedAccount> linked)
    {
        var sut = new AuthController(
            this._humanService.Object, this._profileService.Object, new Mock<IPoracleApiProxy>().Object,
            this._humanProxy.Object, PairTestSupport.Writer(linked), new Mock<ISiteSettingService>().Object,
            new Mock<IWebhookDelegateService>().Object, new Mock<IJwtService>().Object,
            new Mock<IUserRoleResolver>().Object, new Mock<IOidcClient>().Object, new Mock<IOidcSessionService>().Object,
            Options.Create(new DiscordSettings()), Options.Create(new TelegramSettings()), Options.Create(new OidcSettings()),
            Options.Create(new PoracleSettings()), new ConfigurationBuilder().Build(),
            new Mock<IAccountLinkService>().Object, new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider(),
            new Mock<IHttpClientFactory>().Object, NullLogger<AuthController>.Instance);
        SetupUser(sut, Discord);
        return sut;
    }

    private static LinkedPair PairWithTelegram(PauseReason pause) => PairTestSupport.Pair(Discord) with
    {
        Telegram = new PairSide(LinkSide.Telegram, Telegram, true, pause),
    };

    [Fact]
    public async Task ToggleOn_DoesNotStartAStoppedOtherSide()
    {
        this._humanService.Setup(h => h.GetByIdAsync(Discord)).ReturnsAsync(new Human { Id = Discord, Enabled = 0 });

        await this.Auth(PairTestSupport.Linked(PairWithTelegram(PauseReason.Stopped))).ToggleAlerts();

        this._humanProxy.Verify(p => p.StartAsync(Discord), Times.Once);
        this._humanProxy.Verify(p => p.StartAsync(Telegram), Times.Never);
    }

    [Fact]
    public async Task ToggleOn_StartsAnOtherSideNothingPaused()
    {
        this._humanService.Setup(h => h.GetByIdAsync(Discord)).ReturnsAsync(new Human { Id = Discord, Enabled = 0 });

        await this.Auth(PairTestSupport.Linked(PairWithTelegram(PauseReason.None))).ToggleAlerts();

        this._humanProxy.Verify(p => p.StartAsync(Telegram), Times.Once);
    }

    [Fact]
    public async Task ToggleOff_StopsBothEvenWhenTheOtherIsAlreadyPaused()
    {
        this._humanService.Setup(h => h.GetByIdAsync(Discord)).ReturnsAsync(new Human { Id = Discord, Enabled = 1 });

        await this.Auth(PairTestSupport.Linked(PairWithTelegram(PauseReason.Stopped))).ToggleAlerts();

        this._humanProxy.Verify(p => p.StopAsync(Discord), Times.Once);
        this._humanProxy.Verify(p => p.StopAsync(Telegram), Times.Once);
    }

    // ---- drifted profiles

    private ProfileController Profiles(Mock<ILinkedAccount> linked)
    {
        var sut = new ProfileController(
            this._profileService.Object, this._humanService.Object, this._humanProxy.Object, Mock.Of<IJwtService>(),
            Mock.Of<IUserRoleResolver>(), Mock.Of<IUserAreaWriter>(),
            PairTestSupport.Writer(linked), NullLogger<ProfileController>.Instance);
        SetupUser(sut, Discord);
        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync(Discord, 2)).ReturnsAsync(new Profile { Id = Discord, ProfileNo = 2, Name = "Work" });
        return sut;
    }

    [Fact]
    public async Task ProfileDelete_OtherSideHoldsADifferentProfileAtThatNumber_IsLeftAloneWithAWarning()
    {
        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync(Telegram, 2)).ReturnsAsync(new Profile { Id = Telegram, ProfileNo = 2, Name = "Night shift" });
        var sut = this.Profiles(PairTestSupport.Linked(PairTestSupport.Pair(Discord)));

        var result = await sut.Delete(2);

        Assert.IsType<NoContentResult>(result);
        this._humanProxy.Verify(p => p.DeleteProfileAsync(Discord, 2), Times.Once);
        this._humanProxy.Verify(p => p.DeleteProfileAsync(Telegram, It.IsAny<int>()), Times.Never);
        Assert.Contains("\"code\":\"out_of_step\"", Header(sut));
    }

    [Fact]
    public async Task ProfileDelete_NamesAgree_DeletesBoth()
    {
        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync(Telegram, 2)).ReturnsAsync(new Profile { Id = Telegram, ProfileNo = 2, Name = " work " });
        var sut = this.Profiles(PairTestSupport.Linked(PairTestSupport.Pair(Discord)));

        await sut.Delete(2);

        this._humanProxy.Verify(p => p.DeleteProfileAsync(Telegram, 2), Times.Once);
        Assert.Null(Header(sut));
    }

    [Fact]
    public async Task ProfileRename_OtherSideDrifted_IsLeftAloneWithAWarning_AndMissingProfileToo()
    {
        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync(Telegram, 2)).ReturnsAsync(new Profile { Id = Telegram, ProfileNo = 2, Name = "Night shift" });
        var sut = this.Profiles(PairTestSupport.Linked(PairTestSupport.Pair(Discord)));

        await sut.Update(2, new Profile { Name = "Office" });

        this._humanProxy.Verify(p => p.UpdateProfileAsync(Discord, It.IsAny<JsonElement>()), Times.Once);
        this._humanProxy.Verify(p => p.UpdateProfileAsync(Telegram, It.IsAny<JsonElement>()), Times.Never);
        Assert.Contains("\"code\":\"out_of_step\"", Header(sut));

        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync(Telegram, 2)).ReturnsAsync((Profile?)null);
        var missing = this.Profiles(PairTestSupport.Linked(PairTestSupport.Pair(Discord)));
        await missing.Update(2, new Profile { Name = "Office" });
        Assert.Contains("\"code\":\"out_of_step\"", Header(missing));
    }

    [Fact]
    public async Task ProfileRename_NamesAgree_UpdatesBoth()
    {
        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync(Telegram, 2)).ReturnsAsync(new Profile { Id = Telegram, ProfileNo = 2, Name = "Work" });
        var sut = this.Profiles(PairTestSupport.Linked(PairTestSupport.Pair(Discord)));

        await sut.Update(2, new Profile { Name = "Office" });

        this._humanProxy.Verify(p => p.UpdateProfileAsync(Telegram, It.IsAny<JsonElement>()), Times.Once);
        Assert.Null(Header(sut));
    }

    // ---- linked profile import

    private readonly Mock<IProfileOverviewService> _overview = new();

    private ProfileOverviewController Overview(LinkedPair pair)
    {
        var sut = new ProfileOverviewController(
            this._overview.Object, this._profileService.Object, this._humanProxy.Object,
            PairTestSupport.Writer(PairTestSupport.Linked(pair)), LinkedTestKit.Router(PairTestSupport.Linked(pair).Object),
            Mock.Of<IJwtService>(), Mock.Of<IUserRoleResolver>(), NullLogger<ProfileOverviewController>.Instance);
        SetupUser(sut, Discord);
        this._profileService.Setup(s => s.GetByUserAsync(Discord)).ReturnsAsync([]);
        this._overview.Setup(o => o.ImportAlarmsAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<JsonElement>())).ReturnsAsync(2);
        return sut;
    }

    private static ProfileOverviewImportRequest Import() =>
        new("Imported", 1, JsonDocument.Parse("""{"pokemon":[{"pokemon_id":1}]}""").RootElement.Clone());

    [Fact]
    public async Task Import_Linked_AlarmsGoToTheDefaultSidesHuman_AndBothGetTheProfileAndGeography()
    {
        this._humanProxy.Setup(p => p.AddProfileAsync(It.IsAny<string>(), It.IsAny<JsonElement>())).ReturnsAsync(4);
        var pair = PairTestSupport.Pair(Discord, defaultSide: LinkSide.Telegram);

        var result = await this.Overview(pair).ImportProfile(Import());

        Assert.IsType<OkObjectResult>(result);
        this._overview.Verify(o => o.ImportAlarmsAsync(Telegram, 4, It.IsAny<JsonElement>()), Times.Once);
        this._overview.Verify(o => o.ImportAlarmsAsync(Discord, It.IsAny<int>(), It.IsAny<JsonElement>()), Times.Never);
        this._humanProxy.Verify(p => p.SetProfileGeographyAsync(Discord, 4, It.IsAny<IReadOnlyList<string>>(), 0.0, 0.0), Times.Once);
        this._humanProxy.Verify(p => p.SetProfileGeographyAsync(Telegram, 4, It.IsAny<IReadOnlyList<string>>(), 0.0, 0.0), Times.Once);
    }

    [Fact]
    public async Task Import_DefaultSideNotRegistered_AlarmsStayWithTheCaller()
    {
        this._humanProxy.Setup(p => p.AddProfileAsync(It.IsAny<string>(), It.IsAny<JsonElement>())).ReturnsAsync(4);
        var pair = PairTestSupport.Pair(Discord, telegramExists: false, defaultSide: LinkSide.Telegram);

        await this.Overview(pair).ImportProfile(Import());

        this._overview.Verify(o => o.ImportAlarmsAsync(Discord, 4, It.IsAny<JsonElement>()), Times.Once);
        this._humanProxy.Verify(p => p.AddProfileAsync(Telegram, It.IsAny<JsonElement>()), Times.Never);
    }

    [Fact]
    public async Task Import_Unlinked_AlarmsGoToTheCaller()
    {
        this._humanProxy.Setup(p => p.AddProfileAsync(Discord, It.IsAny<JsonElement>())).ReturnsAsync(4);

        await this.Overview(LinkedPair.Single(Discord)).ImportProfile(Import());

        this._overview.Verify(o => o.ImportAlarmsAsync(Discord, 4, It.IsAny<JsonElement>()), Times.Once);
    }

    [Fact]
    public async Task Import_StrayNumberOnTheOtherSide_IsDeleted_AndAlarmsFallBackToTheCaller()
    {
        this._humanProxy.Setup(p => p.AddProfileAsync(Discord, It.IsAny<JsonElement>())).ReturnsAsync(4);
        this._humanProxy.Setup(p => p.AddProfileAsync(Telegram, It.IsAny<JsonElement>())).ReturnsAsync(7);
        var pair = PairTestSupport.Pair(Discord, defaultSide: LinkSide.Telegram);

        var result = await this.Overview(pair).ImportProfile(Import());

        Assert.IsType<OkObjectResult>(result);
        this._humanProxy.Verify(p => p.DeleteProfileAsync(Telegram, 7), Times.Once);
        this._overview.Verify(o => o.ImportAlarmsAsync(Discord, 4, It.IsAny<JsonElement>()), Times.Once);
        this._humanProxy.Verify(p => p.SetProfileGeographyAsync(Telegram, It.IsAny<int>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<double>(), It.IsAny<double>()), Times.Never);
    }

    [Fact]
    public async Task Import_GeographyFailsOnTheAlarmHumanThatIsTheSecondWrite_RollsBackEverything()
    {
        this._humanProxy.Setup(p => p.AddProfileAsync(It.IsAny<string>(), It.IsAny<JsonElement>())).ReturnsAsync(4);
        this._humanProxy.Setup(p => p.SetProfileGeographyAsync(Telegram, 4, It.IsAny<IReadOnlyList<string>>(), It.IsAny<double>(), It.IsAny<double>()))
            .ThrowsAsync(new HttpRequestException("down"));
        var pair = PairTestSupport.Pair(Discord, defaultSide: LinkSide.Telegram);

        await Assert.ThrowsAsync<HttpRequestException>(() => this.Overview(pair).ImportProfile(Import()));

        this._humanProxy.Verify(p => p.DeleteProfileAsync(Discord, 4), Times.Once);
        this._humanProxy.Verify(p => p.DeleteProfileAsync(Telegram, 4), Times.Once);
    }

    [Fact]
    public async Task Import_GeographyFailsOnTheSignedInSide_FailsTheImport()
    {
        this._humanProxy.Setup(p => p.AddProfileAsync(It.IsAny<string>(), It.IsAny<JsonElement>())).ReturnsAsync(4);
        this._humanProxy.Setup(p => p.SetProfileGeographyAsync(Discord, 4, It.IsAny<IReadOnlyList<string>>(), It.IsAny<double>(), It.IsAny<double>()))
            .ThrowsAsync(new HttpRequestException("down"));
        var pair = PairTestSupport.Pair(Discord, defaultSide: LinkSide.Telegram);
        var sut = this.Overview(pair);

        // The signed-in side is the first write, so its failure is the import's failure, as before.
        await Assert.ThrowsAsync<HttpRequestException>(() => sut.ImportProfile(Import()));
    }

    // ---- alerts on/off and places, both humans through the writer

    [Fact]
    public async Task ToggleOff_OtherSideFailing_StillAnswersWithAWarning()
    {
        this._humanService.Setup(h => h.GetByIdAsync(Discord)).ReturnsAsync(new Human { Id = Discord, Enabled = 1 });
        this._humanProxy.Setup(p => p.StopAsync(Telegram)).ThrowsAsync(new HttpRequestException("down"));
        var sut = this.Auth(PairTestSupport.Linked(PairTestSupport.Pair(Discord)));

        var result = await sut.ToggleAlerts();

        Assert.Equal("""{"enabled":false}""", Body(result));
        Assert.Contains("telegram", Header(sut));
    }

    [Fact]
    public async Task Toggle_Unlinked_TouchesOnlyTheCaller()
    {
        this._humanService.Setup(h => h.GetByIdAsync(Discord)).ReturnsAsync(new Human { Id = Discord, Enabled = 0 });

        await this.Auth(PairTestSupport.Unlinked()).ToggleAlerts();

        this._humanProxy.Verify(p => p.StartAsync(Discord), Times.Once);
        this._humanProxy.Verify(p => p.StartAsync(Telegram), Times.Never);
        this._humanProxy.Verify(p => p.StopAsync(It.IsAny<string>()), Times.Never);
    }

    private LocationController Places(Mock<ILinkedAccount> linked)
    {
        this._humanProxy.Setup(p => p.GetPlacesAsync(It.IsAny<string>())).ReturnsAsync(new SavedPlaces());
        return this.Location(linked);
    }

    [Fact]
    public async Task AddPlace_Linked_SavesItForBothHumansCallerFirst()
    {
        var calls = new List<string>();
        this._humanProxy.Setup(p => p.AddPlaceAsync(It.IsAny<string>(), It.IsAny<SavedPlace>()))
            .Callback((string id, SavedPlace _) => calls.Add(id)).ReturnsAsync((string?)null);
        var sut = this.Places(PairTestSupport.Linked(PairTestSupport.Pair(Discord)));

        var result = await sut.AddPlace(new SavedPlace { Label = " Home ", Latitude = 51.5, Longitude = -0.12 });

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal([Discord, Telegram], calls);
        this._humanProxy.Verify(p => p.AddPlaceAsync(Telegram, It.Is<SavedPlace>(s => s.Label == "Home")), Times.Once);
        Assert.Null(Header(sut));
    }

    [Fact]
    public async Task AddPlace_OtherSideRefusing_IsOkWithTheHeader()
    {
        this._humanProxy.Setup(p => p.AddPlaceAsync(Discord, It.IsAny<SavedPlace>())).ReturnsAsync((string?)null);
        this._humanProxy.Setup(p => p.AddPlaceAsync(Telegram, It.IsAny<SavedPlace>())).ReturnsAsync("That name is taken.");
        var sut = this.Places(PairTestSupport.Linked(PairTestSupport.Pair(Discord)));

        var result = await sut.AddPlace(new SavedPlace { Label = "Home", Latitude = 51.5, Longitude = -0.12 });

        Assert.IsType<OkObjectResult>(result);
        Assert.Contains("telegram", Header(sut));
    }

    [Fact]
    public async Task AddPlace_SignedInSideRefusing_Is400AndTheOtherIsNotTried()
    {
        this._humanProxy.Setup(p => p.AddPlaceAsync(Discord, It.IsAny<SavedPlace>())).ReturnsAsync("That name is taken.");
        var sut = this.Places(PairTestSupport.Linked(PairTestSupport.Pair(Discord)));

        var result = await sut.AddPlace(new SavedPlace { Label = "Home", Latitude = 51.5, Longitude = -0.12 });

        Assert.IsType<BadRequestObjectResult>(result);
        this._humanProxy.Verify(p => p.AddPlaceAsync(Telegram, It.IsAny<SavedPlace>()), Times.Never);
    }

    [Fact]
    public async Task MovePlace_Linked_MovesItForBothHumans()
    {
        var sut = this.Places(PairTestSupport.Linked(PairTestSupport.Pair(Discord)));

        var result = await sut.UpdatePlace("Home", new LocationController.PlaceMoveRequest { Latitude = 51.6, Longitude = -0.2 });

        Assert.IsType<OkObjectResult>(result);
        this._humanProxy.Verify(p => p.UpdatePlaceAsync(Discord, "Home", 51.6, -0.2), Times.Once);
        this._humanProxy.Verify(p => p.UpdatePlaceAsync(Telegram, "Home", 51.6, -0.2), Times.Once);
    }

    [Fact]
    public async Task MovePlace_OtherSideFailing_IsOkWithTheHeader()
    {
        this._humanProxy.Setup(p => p.UpdatePlaceAsync(Telegram, "Home", It.IsAny<double>(), It.IsAny<double>()))
            .ThrowsAsync(new PoracleRequestRefusedException("no such place"));
        var sut = this.Places(PairTestSupport.Linked(PairTestSupport.Pair(Discord)));

        Assert.IsType<OkObjectResult>(await sut.UpdatePlace("Home", new LocationController.PlaceMoveRequest { Latitude = 51.6, Longitude = -0.2 }));
        Assert.Contains("no such place", Header(sut));
    }

    [Fact]
    public async Task DeletePlace_Linked_DeletesItForBothHumans()
    {
        var sut = this.Places(PairTestSupport.Linked(PairTestSupport.Pair(Discord)));

        Assert.IsType<NoContentResult>(await sut.DeletePlace("Home"));

        this._humanProxy.Verify(p => p.DeletePlaceAsync(Discord, "Home"), Times.Once);
        this._humanProxy.Verify(p => p.DeletePlaceAsync(Telegram, "Home"), Times.Once);
        Assert.Null(Header(sut));
    }

    [Fact]
    public async Task DeletePlace_SignedInSideInUse_Is409AndTheOtherIsNotTried()
    {
        this._humanProxy.Setup(p => p.DeletePlaceAsync(Discord, "Home")).ThrowsAsync(new PlaceInUseException(["a"]));
        var sut = this.Places(PairTestSupport.Linked(PairTestSupport.Pair(Discord)));

        Assert.IsType<ConflictObjectResult>(await sut.DeletePlace("Home"));
        this._humanProxy.Verify(p => p.DeletePlaceAsync(Telegram, It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Places_Unlinked_OneHumanEach()
    {
        this._humanProxy.Setup(p => p.AddPlaceAsync(It.IsAny<string>(), It.IsAny<SavedPlace>())).ReturnsAsync((string?)null);
        var sut = this.Places(PairTestSupport.Unlinked());

        await sut.AddPlace(new SavedPlace { Label = "Home", Latitude = 1, Longitude = 2 });
        await sut.UpdatePlace("Home", new LocationController.PlaceMoveRequest { Latitude = 3, Longitude = 4 });
        await sut.DeletePlace("Home");

        this._humanProxy.Verify(p => p.AddPlaceAsync(Telegram, It.IsAny<SavedPlace>()), Times.Never);
        this._humanProxy.Verify(p => p.UpdatePlaceAsync(Telegram, It.IsAny<string>(), It.IsAny<double>(), It.IsAny<double>()), Times.Never);
        this._humanProxy.Verify(p => p.DeletePlaceAsync(Telegram, It.IsAny<string>()), Times.Never);
        this._humanProxy.Verify(p => p.DeletePlaceAsync(Discord, "Home"), Times.Once);
    }
}
