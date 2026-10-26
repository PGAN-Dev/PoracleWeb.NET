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
    private readonly Mock<IUserAreaWriter> _areas = new();
    private readonly LinkedHumanProvisioner _sut;
    private JsonElement? _createBody;
    private List<int> _added = [];

    public LinkedHumanProvisionerTests()
    {
        this._proxy.Setup(p => p.GetProfilesAsync(Telegram)).ReturnsAsync(Json("""{"profile":[{"profile_no":1,"name":"Home"}]}"""));
        this._proxy.Setup(p => p.GetPlacesAsync(Discord)).ReturnsAsync(new SavedPlaces
        {
            Named = [new SavedPlace { Label = "gym", Latitude = 51.51, Longitude = -0.11 }],
        });
        this._proxy.Setup(p => p.CreateHumanAsync(It.IsAny<JsonElement>()))
            .Callback((JsonElement b) => this._createBody = b.Clone()).Returns(Task.CompletedTask);
        // By default every name is allowed, so the tests below see the copy itself; the filter has its own test.
        this._areas.Setup(a => a.FilterAllowedAsync(It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>()))
            .ReturnsAsync((string _, IReadOnlyCollection<string> names) => [.. names]);
        this._added = this.UseSource(1, (1, "Home"), (2, "Work"));
        this._sut = new LinkedHumanProvisioner(this._proxy.Object, this._humans.Object, this._areas.Object, NullLogger<LinkedHumanProvisioner>.Instance);
    }

    [Fact]
    public async Task CreateMirror_DropsAnAreaTheNewAccountMayNotSelect_AndKeepsAllowedAndDrawnFences()
    {
        this._humans.Setup(h => h.GetByIdAsync(Discord)).ReturnsAsync(new Human
        {
            Id = Discord, Type = AccountLinkKeys.DiscordUserType, Name = "Old Town", Language = "en",
            Latitude = 51.5, Longitude = -0.12, Area = "[\"old town\",\"my drawn fence\",\"someone elses\"]", CurrentProfileNo = 1,
        });
        this._proxy.Setup(p => p.GetProfilesAsync(Discord)).ReturnsAsync(Json("{\"profile\":[]}"));
        // The new account is not linked yet, so only the person's own id knows their drawn fence.
        this._areas.Setup(a => a.FilterAllowedAsync(Telegram, It.IsAny<IReadOnlyCollection<string>>())).ReturnsAsync(["old town"]);
        this._areas.Setup(a => a.FilterAllowedAsync(Discord, It.IsAny<IReadOnlyCollection<string>>())).ReturnsAsync(["old town", "my drawn fence"]);

        await this._sut.CreateMirrorAsync(Discord, LinkSide.Telegram, Telegram, "Old Town");

        this._proxy.Verify(p => p.SetProfileGeographyAsync(Telegram, 1,
            It.Is<IReadOnlyCollection<string>>(a => a.SequenceEqual(new[] { "old town", "my drawn fence" })), 51.5, -0.12), Times.Once);
    }

    private static JsonElement Json(string s) => JsonDocument.Parse(s).RootElement.Clone();

    private static string Profile(int no, string name, string area, double lat, double lon, string hours) =>
        JsonSerializer.Serialize(new { profile_no = no, name, area, latitude = lat, longitude = lon, active_hours = hours });

    /// <summary>Sets the source profiles and current profile. AddProfileAsync hands out numbers as PoracleNG does: the lowest free one.</summary>
    private List<int> UseSource(int current, params (int No, string Name)[] profiles)
    {
        this._humans.Setup(h => h.GetByIdAsync(Discord)).ReturnsAsync(new Human
        {
            Id = Discord,
            Type = AccountLinkKeys.DiscordUserType,
            Name = "Old Town",
            Language = "en",
            Latitude = 51.5,
            Longitude = -0.12,
            Area = "[\"old town\"]",
            CommunityMembership = "[\"northside\"]",
            CurrentProfileNo = current,
        });
        var rows = profiles.Select(p => Profile(p.No, p.Name, $"[\"area{p.No}\"]", 51 + p.No, -p.No,
            $"[{{\"day\":{p.No},\"hours\":{p.No + 5},\"mins\":0}}]"));
        this._proxy.Setup(p => p.GetProfilesAsync(Discord)).ReturnsAsync(Json("{\"profile\":[" + string.Join(",", rows) + "]}"));

        var held = new List<int> { 1 };
        var added = new List<int>();
        this._proxy.Setup(p => p.AddProfileAsync(Telegram, It.IsAny<JsonElement>()))
            .ReturnsAsync(() =>
            {
                var n = 1;
                while (held.Contains(n))
                {
                    n++;
                }

                held.Add(n);
                added.Add(n);
                return n;
            });
        this._proxy.Setup(p => p.DeleteProfileAsync(Telegram, It.IsAny<int>()))
            .Callback((string _, int n) => held.Remove(n)).Returns(Task.CompletedTask);
        return added;
    }

    [Fact]
    public async Task CopiesExactlyTheDeclaredCreateFields_AndNoArea()
    {
        await this._sut.CreateMirrorAsync(Discord, LinkSide.Telegram, Telegram, "Riverside");

        var body = this._createBody!.Value;
        var names = body.EnumerateObject().Select(p => p.Name).ToList();
        Assert.All(names, n => Assert.Contains(n, _declared));
        foreach (var required in new[] { "type", "id", "name", "language", "latitude", "longitude", "community" })
        {
            Assert.Contains(required, names);
        }

        // Areas go through the trusted per-profile write: an untrusted filter can drop a drawn fence.
        Assert.DoesNotContain("area", names);
        Assert.Equal("telegram:user", body.GetProperty("type").GetString());
        Assert.Equal(Telegram, body.GetProperty("id").GetString());
        Assert.Equal("Riverside", body.GetProperty("name").GetString());
        Assert.Equal(51.5, body.GetProperty("latitude").GetDouble());
        Assert.Equal(["northside"], body.GetProperty("community").EnumerateArray().Select(a => a.GetString()).ToArray());
    }

    [Fact]
    public async Task CreateMirror_WithCommunities_RecordsThoseNotTheSourcesOwn()
    {
        // A qualified Telegram registration records the communities Telegram placed the person in; the Discord
        // person's own membership (northside, by guild role) says nothing about their Telegram groups.
        await this._sut.CreateMirrorAsync(Discord, LinkSide.Telegram, Telegram, "Riverside", ["river", "hill"]);

        var body = this._createBody!.Value;
        Assert.Equal(["river", "hill"], body.GetProperty("community").EnumerateArray().Select(a => a.GetString()).ToArray());
        Assert.All(body.EnumerateObject().Select(p => p.Name), n => Assert.Contains(n, _declared));
    }

    [Fact]
    public async Task CreateMirror_WithNoCommunities_SendsAnEmptyList_NotTheSourcesOwn()
    {
        await this._sut.CreateMirrorAsync(Discord, LinkSide.Telegram, Telegram, "Riverside", []);

        Assert.Empty(this._createBody!.Value.GetProperty("community").EnumerateArray());
    }

    [Fact]
    public async Task ContiguousSource_MirrorsEveryProfileNumber()
    {
        await this._sut.CreateMirrorAsync(Discord, LinkSide.Telegram, Telegram, "Riverside");

        Assert.Equal("Home", this._createBody!.Value.GetProperty("profile_name").GetString());
        this._proxy.Verify(p => p.UpdateProfileAsync(Telegram, It.Is<JsonElement>(b =>
            b.GetProperty("profile_no").GetInt32() == 1 && b.GetProperty("name").GetString() == "Home"
            && b.GetProperty("active_hours").GetString()!.Contains("\"hours\":6"))), Times.Once);
        Assert.Equal([2], this._added);
        this._proxy.Verify(p => p.AddProfileAsync(Telegram, It.Is<JsonElement>(b =>
            b.GetProperty("name").GetString() == "Work" && b.GetProperty("active_hours").GetString()!.Contains("\"hours\":7"))), Times.Once);
        this._proxy.Verify(p => p.SetProfileGeographyAsync(Telegram, 1, It.Is<IReadOnlyCollection<string>>(a => a.Single() == "area1"), 52, -1), Times.Once);
        this._proxy.Verify(p => p.SetProfileGeographyAsync(Telegram, 2, It.Is<IReadOnlyCollection<string>>(a => a.Single() == "area2"), 53, -2), Times.Once);
        this._proxy.Verify(p => p.DeleteProfileAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        this._proxy.Verify(p => p.SwitchProfileAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task SourceWithGap_FillsWithPlaceholderThenDeletesIt()
    {
        var added = this.UseSource(3, (1, "Home"), (3, "Night"));

        await this._sut.CreateMirrorAsync(Discord, LinkSide.Telegram, Telegram, "Riverside");

        Assert.Equal([2, 3], added);
        this._proxy.Verify(p => p.AddProfileAsync(Telegram, It.Is<JsonElement>(b => b.GetProperty("name").GetString() == "Night")), Times.Once);
        this._proxy.Verify(p => p.SetProfileGeographyAsync(Telegram, 3, It.Is<IReadOnlyCollection<string>>(a => a.Single() == "area3"), 54, -3), Times.Once);
        this._proxy.Verify(p => p.SetProfileGeographyAsync(Telegram, 2, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<double>(), It.IsAny<double>()), Times.Never);
        this._proxy.Verify(p => p.SwitchProfileAsync(Telegram, 3), Times.Once);
        this._proxy.Verify(p => p.DeleteProfileAsync(Telegram, 2), Times.Once);
        this._proxy.Verify(p => p.DeleteProfileAsync(Telegram, 3), Times.Never);
    }

    [Fact]
    public async Task CurrentProfileNotLowest_EachNonDefaultProfileAddedOnce_AndSwitched()
    {
        var added = this.UseSource(2, (1, "Home"), (2, "Work"), (3, "Night"));

        await this._sut.CreateMirrorAsync(Discord, LinkSide.Telegram, Telegram, "Riverside");

        Assert.Equal([2, 3], added);
        this._proxy.Verify(p => p.AddProfileAsync(Telegram, It.IsAny<JsonElement>()), Times.Exactly(2));
        this._proxy.Verify(p => p.SwitchProfileAsync(Telegram, 2), Times.Once);
        this._proxy.Verify(p => p.DeleteProfileAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task SourceWithoutProfileOne_DefaultIsAPlaceholderDeletedAfterSwitching()
    {
        this.UseSource(2, (2, "Work"));

        await this._sut.CreateMirrorAsync(Discord, LinkSide.Telegram, Telegram, "Riverside");

        this._proxy.Verify(p => p.AddProfileAsync(Telegram, It.Is<JsonElement>(b => b.GetProperty("name").GetString() == "Work")), Times.Once);
        this._proxy.Verify(p => p.SetProfileGeographyAsync(Telegram, 2, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<double>(), It.IsAny<double>()), Times.Once);
        this._proxy.Verify(p => p.SwitchProfileAsync(Telegram, 2), Times.Once);
        this._proxy.Verify(p => p.DeleteProfileAsync(Telegram, 1), Times.Once);
    }

    [Fact]
    public async Task SourceWithProfileZero_SkipsItAndMirrorsTheRest()
    {
        var added = this.UseSource(0, (0, "Old"), (1, "Home"));

        await this._sut.CreateMirrorAsync(Discord, LinkSide.Telegram, Telegram, "Riverside");

        Assert.Empty(added);
        this._proxy.Verify(p => p.SetProfileGeographyAsync(Telegram, 1, It.Is<IReadOnlyCollection<string>>(a => a.Single() == "area1"), 52, -1), Times.Once);
        this._proxy.Verify(p => p.SetProfileGeographyAsync(Telegram, 0, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<double>(), It.IsAny<double>()), Times.Never);
        this._proxy.Verify(p => p.SwitchProfileAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        this._proxy.Verify(p => p.DeleteProfileAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task SourceWithProfileZeroAndAGap_KeepsTheFillerLogic()
    {
        var added = this.UseSource(1, (0, "Old"), (1, "Home"), (3, "Night"));

        await this._sut.CreateMirrorAsync(Discord, LinkSide.Telegram, Telegram, "Riverside");

        Assert.Equal([2, 3], added);
        this._proxy.Verify(p => p.SetProfileGeographyAsync(Telegram, 3, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<double>(), It.IsAny<double>()), Times.Once);
        this._proxy.Verify(p => p.DeleteProfileAsync(Telegram, 2), Times.Once);
        this._proxy.Verify(p => p.SetProfileGeographyAsync(Telegram, 0, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<double>(), It.IsAny<double>()), Times.Never);
    }

    [Fact]
    public async Task SourceCurrentProfileZero_SwitchesTheMirrorToItsLowestRealProfile()
    {
        this.UseSource(0, (0, "Old"), (2, "Work"), (3, "Night"));

        await this._sut.CreateMirrorAsync(Discord, LinkSide.Telegram, Telegram, "Riverside");

        this._proxy.Verify(p => p.SwitchProfileAsync(Telegram, 2), Times.Once);
        this._proxy.Verify(p => p.SwitchProfileAsync(It.IsAny<string>(), 0), Times.Never);
    }

    [Fact]
    public async Task NoSourceProfileRows_WritesHumanGeographyToTheDefaultProfile()
    {
        this.UseSource(1);

        await this._sut.CreateMirrorAsync(Discord, LinkSide.Telegram, Telegram, "Riverside");

        this._proxy.Verify(p => p.SetProfileGeographyAsync(Telegram, 1, It.Is<IReadOnlyCollection<string>>(a => a.Single() == "old town"), 51.5, -0.12), Times.Once);
        this._proxy.Verify(p => p.AddProfileAsync(It.IsAny<string>(), It.IsAny<JsonElement>()), Times.Never);
    }

    [Fact]
    public async Task ProfileNumberMismatch_RollsBackAndThrowsProvisionFailed()
    {
        this._proxy.Setup(p => p.AddProfileAsync(Telegram, It.IsAny<JsonElement>())).ReturnsAsync(5);
        this._proxy.Setup(p => p.GetHumanAsync(Telegram))
            .ReturnsAsync(Json("""{"id":"200000001","type":"telegram:user","name":"Riverside"}"""));

        var ex = await Assert.ThrowsAsync<LinkRefusalException>(
            () => this._sut.CreateMirrorAsync(Discord, LinkSide.Telegram, Telegram, "Riverside"));

        Assert.Equal(LinkRefusalCode.ProvisionFailed, ex.Code);
        this._proxy.Verify(p => p.DeleteHumanAsync(Telegram), Times.Once);
        this._proxy.Verify(p => p.SwitchProfileAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task CopiesPlaces()
    {
        await this._sut.CreateMirrorAsync(Discord, LinkSide.Telegram, Telegram, "Riverside");

        this._proxy.Verify(p => p.AddPlaceAsync(Telegram, It.Is<SavedPlace>(s => s.Label == "gym" && s.Latitude == 51.51)), Times.Once);
    }

    [Fact]
    public async Task NeverCopiesAlarms()
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
