using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Pgan.PoracleWebNet.Api.Configuration;
using Pgan.PoracleWebNet.Api.Controllers;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Tests.Helpers;

namespace Pgan.PoracleWebNet.Tests.Controllers;

/// <summary>Person-level writes on a linked pair: both humans get them, and a failing second says so.</summary>
public class LinkedPersonWritesTests : ControllerTestBase
{
    private const string Discord = PairTestSupport.DiscordId;
    private const string Telegram = PairTestSupport.TelegramId;

    private readonly Mock<IPoracleHumanProxy> _humanProxy = new();
    private readonly Mock<IHumanService> _humanService = new();
    private readonly Mock<IProfileService> _profileService = new();
    private string? _sutResponseHeader;

    private LocationController Location(Mock<ILinkedAccount> linked)
    {
        var sut = new LocationController(
            this._humanService.Object, this._profileService.Object, this._humanProxy.Object,
            Mock.Of<IPoracleApiProxy>(), PairTestSupport.Writer(linked));
        SetupUser(sut, Discord);
        this._humanService.Setup(h => h.GetByIdAsync(Discord)).ReturnsAsync(new Human { Id = Discord });
        return sut;
    }

    private static string Json(IActionResult result) => JsonSerializer.Serialize(((ObjectResult)result).Value);

    [Fact]
    public async Task Location_Linked_WritesBothHumansCallerFirst()
    {
        var calls = new List<string>();
        this._humanProxy.Setup(p => p.SetLocationAsync(It.IsAny<string>(), 51.5, -0.12))
            .Callback((string id, double _, double _) => calls.Add(id)).Returns(Task.CompletedTask);

        var result = await this.Location(PairTestSupport.Linked(PairTestSupport.Pair(Discord)))
            .UpdateLocation(new LocationController.LocationUpdateRequest { Latitude = 51.5, Longitude = -0.12 });

        Assert.Equal([Discord, Telegram], calls);
        Assert.DoesNotContain("pairWarning", Json(result));
    }

    [Fact]
    public async Task Location_Linked_SecondSideFailing_StillOkWithPairWarning()
    {
        this._humanProxy.Setup(p => p.SetLocationAsync(Telegram, It.IsAny<double>(), It.IsAny<double>()))
            .ThrowsAsync(new HttpRequestException("PoracleNG could not be reached"));

        var sut = this.Location(PairTestSupport.Linked(PairTestSupport.Pair(Discord)));
        var result = await sut.UpdateLocation(new LocationController.LocationUpdateRequest { Latitude = 51.5, Longitude = -0.12 });
        this._sutResponseHeader = sut.Response.Headers["X-Pair-Warning"].ToString();

        var body = JsonDocument.Parse(Json(result)).RootElement;
        Assert.Equal(51.5, body.GetProperty("latitude").GetDouble());
        Assert.False(body.TryGetProperty("pairWarning", out _));
        var header = JsonDocument.Parse(this._sutResponseHeader!).RootElement;
        Assert.Equal("telegram", header.GetProperty("side").GetString());
        Assert.Equal("Poracle couldn't be reached.", header.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Location_SignedInSideFailing_PropagatesAndSkipsTheOther()
    {
        this._humanProxy.Setup(p => p.SetLocationAsync(Discord, It.IsAny<double>(), It.IsAny<double>()))
            .ThrowsAsync(new HttpRequestException("down"));

        await Assert.ThrowsAsync<HttpRequestException>(() => this.Location(PairTestSupport.Linked(PairTestSupport.Pair(Discord)))
            .UpdateLocation(new LocationController.LocationUpdateRequest { Latitude = 1, Longitude = 2 }));

        this._humanProxy.Verify(p => p.SetLocationAsync(Telegram, It.IsAny<double>(), It.IsAny<double>()), Times.Never);
    }

    [Fact]
    public async Task Location_Unlinked_BodyIsExactlyToday()
    {
        var result = await this.Location(PairTestSupport.Unlinked())
            .UpdateLocation(new LocationController.LocationUpdateRequest { Latitude = 51.5, Longitude = -0.12 });

        Assert.Equal("{\"latitude\":51.5,\"longitude\":-0.12}", Json(result));
        this._humanProxy.Verify(p => p.SetLocationAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<double>()), Times.Once);
    }

    [Fact]
    public async Task ProfileCreate_NumbersDiffer_DeletesTheOtherSidesProfileAndWarns()
    {
        var sut = this.ProfileController(PairTestSupport.Linked(PairTestSupport.Pair(Discord)));
        this._humanProxy.Setup(p => p.AddProfileAsync(Discord, It.IsAny<JsonElement>())).ReturnsAsync(1);
        this._humanProxy.Setup(p => p.AddProfileAsync(Telegram, It.IsAny<JsonElement>())).ReturnsAsync(2);
        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync(Discord, 1)).ReturnsAsync(new Profile { Id = Discord, ProfileNo = 1, Name = "Work" });

        var result = await sut.Create(new Profile { Name = "Work" });

        var created = Assert.IsType<CreatedAtActionResult>(result);
        Assert.False(JsonDocument.Parse(JsonSerializer.Serialize(created.Value)).RootElement.TryGetProperty("pairWarning", out _));
        Assert.Contains("\"code\":\"out_of_step\"", sut.Response.Headers["X-Pair-Warning"].ToString());
        this._humanProxy.Verify(p => p.DeleteProfileAsync(Telegram, 2), Times.Once);
        this._humanProxy.Verify(p => p.DeleteProfileAsync(Discord, It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task ProfileCreate_NumbersAgree_KeepsBothAndAddsNoWarning()
    {
        var sut = this.ProfileController(PairTestSupport.Linked(PairTestSupport.Pair(Discord)));
        this._humanProxy.Setup(p => p.AddProfileAsync(It.IsAny<string>(), It.IsAny<JsonElement>())).ReturnsAsync(3);
        var profile = new Profile { Id = Discord, ProfileNo = 3, Name = "Work" };
        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync(Discord, 3)).ReturnsAsync(profile);

        var result = await sut.Create(new Profile { Name = "Work" });

        Assert.Same(profile, Assert.IsType<CreatedAtActionResult>(result).Value);
        this._humanProxy.Verify(p => p.DeleteProfileAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        this._humanProxy.Verify(p => p.AddProfileAsync(Telegram, It.IsAny<JsonElement>()), Times.Once);
    }

    [Fact]
    public async Task ProfileCreate_Linked_OtherSidesAreasAreFilteredForThatHuman()
    {
        var areas = new Mock<IUserAreaWriter>();
        areas.Setup(a => a.FilterAllowedAsync(Discord, It.IsAny<IReadOnlyCollection<string>>()))
            .ReturnsAsync((string _, IReadOnlyCollection<string> n) => n.ToList());
        // "my fence" is the caller's own drawn fence; the other human may only select "old town".
        areas.Setup(a => a.FilterAllowedAsync(Telegram, It.IsAny<IReadOnlyCollection<string>>()))
            .ReturnsAsync(["old town"]);
        var sut = this.ProfileController(PairTestSupport.Linked(PairTestSupport.Pair(Discord)), areas.Object);
        this._humanProxy.Setup(p => p.AddProfileAsync(It.IsAny<string>(), It.IsAny<JsonElement>())).ReturnsAsync(3);
        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync(Discord, 3)).ReturnsAsync(new Profile { Id = Discord, ProfileNo = 3, Name = "Work" });

        await sut.Create(new Profile { Name = "Work", Area = "[\"old town\",\"my fence\"]" });

        areas.Verify(a => a.FilterAllowedAsync(Telegram, It.Is<IReadOnlyCollection<string>>(n => n.SequenceEqual(new[] { "old town", "my fence" }))), Times.Once);
        this._humanProxy.Verify(p => p.SetProfileGeographyAsync(Discord, 3, It.Is<IReadOnlyCollection<string>>(l => l.SequenceEqual(new[] { "old town", "my fence" })), It.IsAny<double>(), It.IsAny<double>()), Times.Once);
        this._humanProxy.Verify(p => p.SetProfileGeographyAsync(Telegram, 3, It.Is<IReadOnlyCollection<string>>(l => l.SequenceEqual(new[] { "old town" })), It.IsAny<double>(), It.IsAny<double>()), Times.Once);
    }

    [Fact]
    public async Task ProfileCreate_Unlinked_FiltersOnceForTheCaller()
    {
        var areas = new Mock<IUserAreaWriter>();
        areas.Setup(a => a.FilterAllowedAsync(Discord, It.IsAny<IReadOnlyCollection<string>>()))
            .ReturnsAsync((string _, IReadOnlyCollection<string> n) => n.ToList());
        var sut = this.ProfileController(PairTestSupport.Unlinked(), areas.Object);
        this._humanProxy.Setup(p => p.AddProfileAsync(Discord, It.IsAny<JsonElement>())).ReturnsAsync(3);
        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync(Discord, 3)).ReturnsAsync(new Profile { Id = Discord, ProfileNo = 3, Name = "Work" });

        await sut.Create(new Profile { Name = "Work", Area = "[\"old town\"]" });

        areas.Verify(a => a.FilterAllowedAsync(It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>()), Times.Once);
    }

    private ProfileController ProfileController(Mock<ILinkedAccount> linked, IUserAreaWriter? areas = null)
    {
        var sut = new ProfileController(
            this._profileService.Object, this._humanService.Object, this._humanProxy.Object, Mock.Of<IJwtService>(),
            Mock.Of<Pgan.PoracleWebNet.Api.Services.IUserRoleResolver>(), areas ?? Mock.Of<IUserAreaWriter>(),
            PairTestSupport.Writer(linked), Mock.Of<Pgan.PoracleWebNet.Core.Abstractions.Services.IUserAreaPolicy>(), NullLogger<ProfileController>.Instance);
        SetupUser(sut, Discord);
        return sut;
    }

    [Fact]
    public async Task MuteDelete_SendsEachHumansOwnListedValue()
    {
        var mutes = new Mock<IPoracleMuteProxy>();
        mutes.Setup(m => m.DeleteAsync(Discord, "area", "aberdeen", null, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        mutes.Setup(m => m.ListAsync(Telegram, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new Mute { Scope = "area", Value = "Aberdeen" }]);
        mutes.Setup(m => m.DeleteAsync(Telegram, "area", "Aberdeen", null, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var sut = new MuteController(mutes.Object, PairTestSupport.Writer(PairTestSupport.Linked(PairTestSupport.Pair(Discord))));
        SetupUser(sut, Discord);

        var result = await sut.DeleteMute("area", "aberdeen", null, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        mutes.Verify(m => m.DeleteAsync(Telegram, "area", "Aberdeen", null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MuteDelete_Unlinked_SendsTheValueAsGiven_WithNoListRead()
    {
        var mutes = new Mock<IPoracleMuteProxy>();
        mutes.Setup(m => m.DeleteAsync(Discord, "area", "Aberdeen", null, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var sut = new MuteController(mutes.Object, PairTestSupport.UnlinkedWriter());
        SetupUser(sut, Discord);

        await sut.DeleteMute("area", "Aberdeen", null, CancellationToken.None);

        mutes.Verify(m => m.ListAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        mutes.Verify(m => m.DeleteAsync(Discord, "area", "Aberdeen", null, It.IsAny<CancellationToken>()), Times.Once);
    }
}
