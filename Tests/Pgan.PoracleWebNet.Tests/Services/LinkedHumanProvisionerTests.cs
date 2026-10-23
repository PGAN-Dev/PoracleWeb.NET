using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

public class LinkedHumanProvisionerTests
{
    private const string Discord = "100000000000000001";
    private const string Telegram = "200000001";

    private static readonly string[] _declared =
    [
        "admin_disable", "area", "community", "enabled", "id", "language",
        "latitude", "longitude", "name", "notes", "profile_name", "type",
    ];

    private readonly Mock<IPoracleHumanProxy> _proxy = new();
    private readonly Mock<IHumanService> _humans = new();
    private readonly LinkedHumanProvisioner _sut;
    private JsonElement? _createBody;

    public LinkedHumanProvisionerTests()
    {
        this._humans.Setup(h => h.GetByIdAsync(Discord)).ReturnsAsync(new Human
        {
            Id = Discord,
            Type = AccountLinkKeys.DiscordUserType,
            Name = "Old Town",
            Language = "en",
            Latitude = 51.5,
            Longitude = -0.12,
            Area = "[\"old town\",\"riverside\"]",
            CommunityMembership = "[\"northside\"]",
            CurrentProfileNo = 1,
        });
        this._proxy.Setup(p => p.GetProfilesAsync(Discord)).ReturnsAsync(Json("""
            {"profile":[
              {"profile_no":1,"name":"Home","area":"[\"old town\"]","latitude":51.5,"longitude":-0.12,"active_hours":"[{\"day\":1,\"hours\":8,\"mins\":0}]"},
              {"profile_no":2,"name":"Work","area":"[\"riverside\"]","latitude":51.6,"longitude":-0.2,"active_hours":"[{\"day\":2,\"hours\":9,\"mins\":30}]"}]}
            """));
        this._proxy.Setup(p => p.GetProfilesAsync(Telegram)).ReturnsAsync(Json("""{"profile":[{"profile_no":1,"name":"Home"}]}"""));
        this._proxy.Setup(p => p.GetPlacesAsync(Discord)).ReturnsAsync(new SavedPlaces
        {
            Named = [new SavedPlace { Label = "gym", Latitude = 51.51, Longitude = -0.11 }],
        });
        this._proxy.Setup(p => p.CreateHumanAsync(It.IsAny<JsonElement>()))
            .Callback((JsonElement b) => this._createBody = b.Clone()).Returns(Task.CompletedTask);
        this._proxy.Setup(p => p.AddProfileAsync(Telegram, It.IsAny<JsonElement>())).ReturnsAsync(2);
        this._sut = new LinkedHumanProvisioner(this._proxy.Object, this._humans.Object, NullLogger<LinkedHumanProvisioner>.Instance);
    }

    private static JsonElement Json(string s) => JsonDocument.Parse(s).RootElement.Clone();

    [Fact]
    public async Task CopiesExactlyTheDeclaredCreateFields()
    {
        await this._sut.CreateMirrorAsync(Discord, LinkSide.Telegram, Telegram, "Riverside");

        var body = this._createBody!.Value;
        var names = body.EnumerateObject().Select(p => p.Name).ToList();
        Assert.All(names, n => Assert.Contains(n, _declared));
        foreach (var required in new[] { "type", "id", "name", "language", "latitude", "longitude", "area", "community" })
        {
            Assert.Contains(required, names);
        }

        Assert.Equal("telegram:user", body.GetProperty("type").GetString());
        Assert.Equal(Telegram, body.GetProperty("id").GetString());
        Assert.Equal("Riverside", body.GetProperty("name").GetString());
        Assert.Equal(51.5, body.GetProperty("latitude").GetDouble());
        Assert.Equal(["old town", "riverside"], body.GetProperty("area").EnumerateArray().Select(a => a.GetString()).ToArray());
        Assert.Equal(["northside"], body.GetProperty("community").EnumerateArray().Select(a => a.GetString()).ToArray());
    }

    [Fact]
    public async Task CopiesProfilesWithNamesSchedulesAndGeography()
    {
        await this._sut.CreateMirrorAsync(Discord, LinkSide.Telegram, Telegram, "Riverside");

        // Profile 1 is the active one, so it is the new human's default: named at create, schedule updated.
        Assert.Equal("Home", this._createBody!.Value.GetProperty("profile_name").GetString());
        this._proxy.Verify(p => p.UpdateProfileAsync(Telegram, It.Is<JsonElement>(b =>
            b.GetProperty("profile_no").GetInt32() == 1 && b.GetProperty("active_hours").GetString()!.Contains("\"hours\":8"))), Times.Once);

        // Profile 2 is created beside it with its own name, schedule, areas and location.
        this._proxy.Verify(p => p.AddProfileAsync(Telegram, It.Is<JsonElement>(b =>
            b.GetProperty("name").GetString() == "Work" && b.GetProperty("active_hours").GetString()!.Contains("\"mins\":30"))), Times.Once);
        this._proxy.Verify(p => p.SetProfileGeographyAsync(
            Telegram, 2, It.Is<IReadOnlyCollection<string>>(a => a.Single() == "riverside"), 51.6, -0.2), Times.Once);
    }

    [Fact]
    public async Task CopiesPlaces()
    {
        await this._sut.CreateMirrorAsync(Discord, LinkSide.Telegram, Telegram, "Riverside");

        this._proxy.Verify(p => p.AddPlaceAsync(Telegram, It.Is<SavedPlace>(s => s.Label == "gym" && s.Latitude == 51.51)), Times.Once);
    }

    [Fact]
    public async Task NeverReadsOrWritesAlarms()
    {
        await this._sut.CreateMirrorAsync(Discord, LinkSide.Telegram, Telegram, "Riverside");

        this._proxy.Verify(p => p.CopyProfileAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task ProfileCopyFailure_DeletesCreatedHumanAndThrowsProvisionFailed()
    {
        this._proxy.Setup(p => p.AddProfileAsync(Telegram, It.IsAny<JsonElement>())).ThrowsAsync(new HttpRequestException("boom"));
        this._proxy.Setup(p => p.GetHumanAsync(Telegram))
            .ReturnsAsync(Json("""{"id":"200000001","type":"telegram:user","name":"Riverside"}"""));

        var ex = await Assert.ThrowsAsync<LinkRefusalException>(
            () => this._sut.CreateMirrorAsync(Discord, LinkSide.Telegram, Telegram, "Riverside"));

        Assert.Equal(LinkRefusalCode.ProvisionFailed, ex.Code);
        Assert.Equal("Nothing was linked.", ex.Message);
        this._proxy.Verify(p => p.DeleteHumanAsync(Telegram), Times.Once);
    }

    [Fact]
    public async Task Rollback_SkipsDelete_WhenNameNoLongerMatches()
    {
        this._proxy.Setup(p => p.AddProfileAsync(Telegram, It.IsAny<JsonElement>())).ThrowsAsync(new HttpRequestException("boom"));
        this._proxy.Setup(p => p.GetHumanAsync(Telegram))
            .ReturnsAsync(Json("""{"id":"200000001","type":"telegram:user","name":"Someone Else"}"""));

        await Assert.ThrowsAsync<LinkRefusalException>(
            () => this._sut.CreateMirrorAsync(Discord, LinkSide.Telegram, Telegram, "Riverside"));

        this._proxy.Verify(p => p.DeleteHumanAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Rollback_SkipsDelete_WhenTypeNoLongerMatches()
    {
        this._proxy.Setup(p => p.CreateHumanAsync(It.IsAny<JsonElement>())).ThrowsAsync(new HttpRequestException("boom"));
        this._proxy.Setup(p => p.GetHumanAsync(Telegram))
            .ReturnsAsync(Json("""{"id":"200000001","type":"telegram:group","name":"Riverside"}"""));

        await Assert.ThrowsAsync<LinkRefusalException>(
            () => this._sut.CreateMirrorAsync(Discord, LinkSide.Telegram, Telegram, "Riverside"));

        this._proxy.Verify(p => p.DeleteHumanAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task RollbackItselfFailing_StillAnswersProvisionFailed()
    {
        this._proxy.Setup(p => p.CreateHumanAsync(It.IsAny<JsonElement>())).ThrowsAsync(new HttpRequestException("boom"));
        this._proxy.Setup(p => p.GetHumanAsync(Telegram)).ThrowsAsync(new HttpRequestException("down"));

        var ex = await Assert.ThrowsAsync<LinkRefusalException>(
            () => this._sut.CreateMirrorAsync(Discord, LinkSide.Telegram, Telegram, "Riverside"));

        Assert.Equal(LinkRefusalCode.ProvisionFailed, ex.Code);
    }
}
