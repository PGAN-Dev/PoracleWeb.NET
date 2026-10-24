using System.Text.Json;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

public class PairDriftServiceTests
{
    private const string Discord = "100000000000000001";
    private const string Telegram = "200000001";

    private static readonly AccountLink Link = new(1, Discord, Telegram, LinkSide.Discord, DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow);

    private readonly Mock<IPoracleHumanProxy> _human = new();
    private readonly Mock<IPoracleTrackingProxy> _tracking = new();
    private readonly PairDriftService _sut;
    private readonly Dictionary<string, SideData> _data = new()
    {
        [Discord] = new(),
        [Telegram] = new(),
    };

    public PairDriftServiceTests()
    {
        this._human.Setup(p => p.GetHumanAsync(It.IsAny<string>()))
            .ReturnsAsync((string id) => Json(JsonSerializer.Serialize(new { latitude = this._data[id].Lat, longitude = this._data[id].Lon, current_profile_no = this._data[id].Current })));
        this._human.Setup(p => p.GetAreaListAsync(It.IsAny<string>())).ReturnsAsync((string id) => (IReadOnlyList<string>)this._data[id].Areas);
        this._human.Setup(p => p.GetProfilesAsync(It.IsAny<string>())).ReturnsAsync((string id) => Json(this._data[id].ProfilesJson()));
        this._human.Setup(p => p.GetPlacesAsync(It.IsAny<string>())).ReturnsAsync((string id) => new SavedPlaces { Named = this._data[id].Places });
        this._tracking.Setup(t => t.GetAllTrackingAllProfilesAsync(It.IsAny<string>()))
            .ReturnsAsync((string id) => Json(JsonSerializer.Serialize(new { pokemon = this._data[id].Rules.Select(n => new { uid = 1, profile_no = n }), raid = Array.Empty<object>(), status = "ok" })));
        this._sut = new PairDriftService(this._human.Object, this._tracking.Object);
    }

    private static JsonElement Json(string s) => JsonDocument.Parse(s).RootElement.Clone();

    private static LinkedPair Pair(PauseReason telegramPause = PauseReason.None) => new(
        Discord, Link,
        new PairSide(LinkSide.Discord, Discord, true, PauseReason.None),
        new PairSide(LinkSide.Telegram, Telegram, true, telegramPause));

    private async Task<string> FingerprintOf(DriftField field) =>
        (await this._sut.GetAsync(Pair())).Single(i => i.Field == field).Fingerprint;

    [Fact]
    public async Task MatchingPair_NoDrift() => Assert.Empty(await this._sut.GetAsync(Pair()));

    [Fact]
    public async Task LocationWithin1e6_NoDrift()
    {
        this._data[Telegram].Lat = 51.500001;

        Assert.Empty(await this._sut.GetAsync(Pair()));
    }

    [Fact]
    public async Task AreasDifferOnlyByCaseAndUnderscore_NoDrift()
    {
        this._data[Discord].Areas = ["old_town", "Northside"];
        this._data[Telegram].Areas = ["northside", "OLD TOWN "];

        Assert.Empty(await this._sut.GetAsync(Pair()));
    }

    [Fact]
    public async Task ActiveHoursStringVsInt_NoDrift()
    {
        this._data[Discord].Profiles = [new ProfileData(1, "Home", """[{"day":1,"hours":8,"mins":0}]""")];
        this._data[Telegram].Profiles = [new ProfileData(1, "Home", """[{"day":"1","hours":"8","mins":"0"}]""")];

        Assert.Empty(await this._sut.GetAsync(Pair()));
    }

    [Fact]
    public async Task LocationDiffers_OneItem()
    {
        this._data[Telegram].Lat = 51.6;

        var item = Assert.Single(await this._sut.GetAsync(Pair()));

        Assert.Equal(DriftField.Location, item.Field);
        Assert.Equal(51.5, item.Discord.GetProperty("lat").GetDouble());
        Assert.Equal(51.6, item.Telegram.GetProperty("lat").GetDouble());
        Assert.Equal(64, item.Fingerprint.Length);
    }

    [Fact]
    public async Task PausedSide_NotDrift() => Assert.Empty(await this._sut.GetAsync(Pair(PauseReason.Stopped)));

    [Fact]
    public async Task Unlinked_NoProxyCalls()
    {
        var items = await this._sut.GetAsync(LinkedPair.Single(Discord));

        Assert.Empty(items);
        this._human.VerifyNoOtherCalls();
        this._tracking.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task MissingSide_NoDriftAndNoCalls()
    {
        var pair = new LinkedPair(Discord, Link,
            new PairSide(LinkSide.Discord, Discord, true, PauseReason.None),
            new PairSide(LinkSide.Telegram, Telegram, false, PauseReason.NotRegistered));

        Assert.Empty(await this._sut.GetAsync(pair));
        this._human.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ResolveLocation_KeepTelegram_WritesDiscordOnly()
    {
        this._data[Telegram].Lat = 51.6;
        var fp = await this.FingerprintOf(DriftField.Location);

        await this._sut.ResolveAsync(Pair(), DriftField.Location, LinkSide.Telegram, fp);

        this._human.Verify(p => p.SetLocationAsync(Discord, 51.6, -0.12), Times.Once);
        this._human.Verify(p => p.SetLocationAsync(Telegram, It.IsAny<double>(), It.IsAny<double>()), Times.Never);
    }

    [Fact]
    public async Task ResolveAreas_KeepDiscord_WritesTelegram()
    {
        this._data[Discord].Areas = ["old town", "northside"];
        this._data[Telegram].Areas = ["riverside"];
        var fp = await this.FingerprintOf(DriftField.Areas);

        await this._sut.ResolveAsync(Pair(), DriftField.Areas, LinkSide.Discord, fp);

        this._human.Verify(p => p.SetAreasTrustedAsync(Telegram, It.Is<IReadOnlyCollection<string>>(a => a.SequenceEqual(new[] { "old town", "northside" }))), Times.Once);
        this._human.Verify(p => p.SetAreasTrustedAsync(Discord, It.IsAny<IReadOnlyCollection<string>>()), Times.Never);
    }

    [Fact]
    public async Task ResolveCurrentProfile_SwitchesOther()
    {
        this._data[Discord].Profiles = [new ProfileData(1, "Home"), new ProfileData(2, "Work")];
        this._data[Telegram].Profiles = [new ProfileData(1, "Home"), new ProfileData(2, "Work")];
        this._data[Discord].Current = 2;
        var fp = await this.FingerprintOf(DriftField.CurrentProfile);

        await this._sut.ResolveAsync(Pair(), DriftField.CurrentProfile, LinkSide.Discord, fp);

        this._human.Verify(p => p.SwitchProfileAsync(Telegram, 2), Times.Once);
        this._human.Verify(p => p.SwitchProfileAsync(Discord, It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task ResolveProfiles_CreatesMissingAndCopiesSchedules()
    {
        this._data[Discord].Profiles = [new ProfileData(1, "Home"), new ProfileData(2, "Work", """[{"day":1,"hours":8,"mins":30}]""", "[\"old town\"]", 51.7, -0.2)];
        this._data[Telegram].Profiles = [new ProfileData(1, "Home")];
        this._human.Setup(p => p.AddProfileAsync(Telegram, It.IsAny<JsonElement>())).ReturnsAsync(2);
        var fp = await this.FingerprintOf(DriftField.Profiles);

        await this._sut.ResolveAsync(Pair(), DriftField.Profiles, LinkSide.Discord, fp);

        this._human.Verify(p => p.AddProfileAsync(Telegram, It.Is<JsonElement>(b =>
            b.GetProperty("name").GetString() == "Work" && b.GetProperty("active_hours").GetString()!.Contains("\"hours\":8"))), Times.Once);
        this._human.Verify(p => p.SetProfileGeographyAsync(Telegram, 2, It.Is<IReadOnlyCollection<string>>(a => a.SequenceEqual(new[] { "old town" })), 51.7, -0.2), Times.Once);
        this._human.Verify(p => p.AddProfileAsync(Discord, It.IsAny<JsonElement>()), Times.Never);
    }

    [Fact]
    public async Task ResolveProfiles_NumberMismatch_DeletesStrayAndRefuses()
    {
        this._data[Discord].Profiles = [new ProfileData(1, "Home"), new ProfileData(3, "Work")];
        this._data[Telegram].Profiles = [new ProfileData(1, "Home")];
        this._human.Setup(p => p.AddProfileAsync(Telegram, It.IsAny<JsonElement>())).ReturnsAsync(2);
        var fp = await this.FingerprintOf(DriftField.Profiles);

        await Assert.ThrowsAsync<PoracleRequestRefusedException>(() => this._sut.ResolveAsync(Pair(), DriftField.Profiles, LinkSide.Discord, fp));

        this._human.Verify(p => p.DeleteProfileAsync(Telegram, 2), Times.Once);
    }

    [Fact]
    public async Task ResolveProfiles_EmptyExtraDeleted()
    {
        this._data[Discord].Profiles = [new ProfileData(1, "Home")];
        this._data[Telegram].Profiles = [new ProfileData(1, "Home"), new ProfileData(2, "Spare")];
        var fp = await this.FingerprintOf(DriftField.Profiles);

        await this._sut.ResolveAsync(Pair(), DriftField.Profiles, LinkSide.Discord, fp);

        this._human.Verify(p => p.DeleteProfileAsync(Telegram, 2), Times.Once);
    }

    [Fact]
    public async Task ResolveProfiles_ExtraWithAlarms_Refused()
    {
        this._data[Discord].Profiles = [new ProfileData(1, "Home")];
        this._data[Telegram].Profiles = [new ProfileData(1, "Home"), new ProfileData(2, "Spare")];
        this._data[Telegram].Rules = [1, 2, 2];
        var fp = await this.FingerprintOf(DriftField.Profiles);

        var ex = await Assert.ThrowsAsync<DriftProfileHoldsAlarmsException>(() => this._sut.ResolveAsync(Pair(), DriftField.Profiles, LinkSide.Discord, fp));

        Assert.Equal(2, ex.Count);
        Assert.Equal("Spare", ex.ProfileName);
        this._human.Verify(p => p.DeleteProfileAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task Resolve_StaleExpected_Throws_AndWritesNothing()
    {
        this._data[Telegram].Lat = 51.6;

        await Assert.ThrowsAsync<DriftChangedException>(() => this._sut.ResolveAsync(Pair(), DriftField.Location, LinkSide.Telegram, "stale"));

        this._human.Verify(p => p.SetLocationAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<double>()), Times.Never);
    }

    [Fact]
    public async Task Resolve_NoLongerDrifting_Throws() =>
        await Assert.ThrowsAsync<DriftChangedException>(() => this._sut.ResolveAsync(Pair(), DriftField.Location, LinkSide.Telegram, "anything"));

    [Fact]
    public async Task ResolvePlaces_KeepDiscord_UpdatesAddsAndRemovesOnTelegram()
    {
        this._data[Discord].Places =
        [
            new SavedPlace { Label = "gym", Latitude = 51.5, Longitude = -0.1 },
            new SavedPlace { Label = "park", Latitude = 51.4, Longitude = -0.3 },
        ];
        this._data[Telegram].Places =
        [
            new SavedPlace { Label = "gym", Latitude = 51.9, Longitude = -0.1 },
            new SavedPlace { Label = "extra", Latitude = 1, Longitude = 1 },
        ];
        var fp = await this.FingerprintOf(DriftField.Places);

        await this._sut.ResolveAsync(Pair(), DriftField.Places, LinkSide.Discord, fp);

        this._human.Verify(p => p.UpdatePlaceAsync(Telegram, "gym", 51.5, -0.1), Times.Once);
        this._human.Verify(p => p.AddPlaceAsync(Telegram, It.Is<SavedPlace>(s => s.Label == "park")), Times.Once);
        this._human.Verify(p => p.DeletePlaceAsync(Telegram, "extra"), Times.Once);
    }

    private sealed record ProfileData(int No, string Name, string Hours = "[]", string Area = "[]", double Lat = 51.5, double Lon = -0.12);

    private sealed class SideData
    {
        public double Lat { get; set; } = 51.5;
        public double Lon { get; set; } = -0.12;
        public int Current { get; set; } = 1;
        public List<string> Areas { get; set; } = ["old town"];
        public List<SavedPlace> Places { get; set; } = [];
        public List<int> Rules { get; set; } = [];
        public List<ProfileData> Profiles { get; set; } = [new ProfileData(1, "Home")];

        public string ProfilesJson() => JsonSerializer.Serialize(new
        {
            profile = this.Profiles.Select(p => new { profile_no = p.No, name = p.Name, area = p.Area, latitude = p.Lat, longitude = p.Lon, active_hours = p.Hours }),
        });
    }
}
