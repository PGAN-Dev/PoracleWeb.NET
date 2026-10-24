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
    private readonly Mock<IUserAreaWriter> _areas = new();
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
        this._tracking.Setup(t => t.GetStoredRulesAsync(It.IsAny<string>()))
            .ReturnsAsync((string id) => (IReadOnlyList<StoredRule>)[
                .. this._data[id].Rules.Select(n => new StoredRule("monster", n, 1, default)),
                .. this._data[id].PlaceRefs.Select(l => new StoredRule("pokemon", 1, 1, Json(JsonSerializer.Serialize(new { override_location_label = l })))),
                .. this._data[id].OtherProfilePlaceRefs.Select(l => new StoredRule("raid", 2, 7, Json(JsonSerializer.Serialize(new { override_location_label = l })))),
            ]);
        this._areas.Setup(a => a.FilterAllowedAsync(It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>()))
            .ReturnsAsync((string _, IReadOnlyCollection<string> names) => (IReadOnlyList<string>)names.ToList());
        this._areas.Setup(a => a.SaveSelectionAsync(It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>()))
            .ReturnsAsync((string _, IReadOnlyCollection<string> names) => new AreaSaveResult(names.ToList(), []));
        this._sut = new PairDriftService(this._human.Object, this._tracking.Object, this._areas.Object);
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

        // Authorised by PoracleWeb.NET's own policy, never written straight through the trusted proxy route.
        this._areas.Verify(a => a.SaveSelectionAsync(Telegram, It.Is<IReadOnlyCollection<string>>(n => n.SequenceEqual(new[] { "old town", "northside" }))), Times.Once);
        this._areas.Verify(a => a.SaveSelectionAsync(Discord, It.IsAny<IReadOnlyCollection<string>>()), Times.Never);
        this._human.Verify(p => p.SetAreasTrustedAsync(It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>()), Times.Never);
    }

    [Fact]
    public async Task ResolveAreas_ANameTheOtherSideMayNotHold_IsRefusedAndNothingIsPosted()
    {
        this._data[Discord].Areas = ["old town", "northside"];
        this._data[Telegram].Areas = ["riverside"];
        this._areas.Setup(a => a.SaveSelectionAsync(Telegram, It.IsAny<IReadOnlyCollection<string>>()))
            .ThrowsAsync(new AreaNotPermittedException(["northside"]));
        var fp = await this.FingerprintOf(DriftField.Areas);

        await Assert.ThrowsAsync<AreaNotPermittedException>(() => this._sut.ResolveAsync(Pair(), DriftField.Areas, LinkSide.Discord, fp));

        this._human.Verify(p => p.SetAreasTrustedAsync(It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>()), Times.Never);
    }

    [Fact]
    public async Task ResolveProfiles_ProfileAreasAreFilteredThroughTheWriterBeforeTheyAreWritten()
    {
        this._data[Discord].Profiles = [new ProfileData(1, "Home"), new ProfileData(2, "Work", Area: "[\"old town\",\"northside\"]", Lat: 51.7, Lon: -0.2)];
        this._data[Telegram].Profiles = [new ProfileData(1, "Home")];
        this._human.Setup(p => p.AddProfileAsync(Telegram, It.IsAny<JsonElement>())).ReturnsAsync(2);
        this._areas.Setup(a => a.FilterAllowedAsync(Telegram, It.IsAny<IReadOnlyCollection<string>>()))
            .ReturnsAsync(["old town"]);
        var fp = await this.FingerprintOf(DriftField.Profiles);

        await this._sut.ResolveAsync(Pair(), DriftField.Profiles, LinkSide.Discord, fp);

        this._areas.Verify(a => a.FilterAllowedAsync(Telegram, It.Is<IReadOnlyCollection<string>>(n => n.SequenceEqual(new[] { "old town", "northside" }))), Times.Once);
        this._human.Verify(p => p.SetProfileGeographyAsync(Telegram, 2, It.Is<IReadOnlyCollection<string>>(a => a.SequenceEqual(new[] { "old town" })), 51.7, -0.2), Times.Once);
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
    public async Task ResolveProfiles_GapInNumbering_CreatesFillerThenDeletesIt()
    {
        this._data[Discord].Profiles = [new ProfileData(1, "Home"), new ProfileData(3, "Work", Area: "[\"old town\"]", Lat: 51.7, Lon: -0.2)];
        this._data[Telegram].Profiles = [new ProfileData(1, "Home")];
        this._human.SetupSequence(p => p.AddProfileAsync(Telegram, It.IsAny<JsonElement>())).ReturnsAsync(2).ReturnsAsync(3);
        var fp = await this.FingerprintOf(DriftField.Profiles);

        await this._sut.ResolveAsync(Pair(), DriftField.Profiles, LinkSide.Discord, fp);

        this._human.Verify(p => p.AddProfileAsync(Telegram, It.IsAny<JsonElement>()), Times.Exactly(2));
        this._human.Verify(p => p.AddProfileAsync(Telegram, It.Is<JsonElement>(b => b.GetProperty("name").GetString() == "Work")), Times.Once);
        this._human.Verify(p => p.SetProfileGeographyAsync(Telegram, 3, It.IsAny<IReadOnlyCollection<string>>(), 51.7, -0.2), Times.Once);
        this._human.Verify(p => p.DeleteProfileAsync(Telegram, 2), Times.Once);
        this._human.Verify(p => p.DeleteProfileAsync(Telegram, 3), Times.Never);
    }

    [Fact]
    public async Task ResolveProfiles_ImpossibleNumber_DeletesStrayAndRefuses409()
    {
        this._data[Discord].Profiles = [new ProfileData(1, "Home"), new ProfileData(2, "Work")];
        this._data[Telegram].Profiles = [new ProfileData(1, "Home")];
        this._human.Setup(p => p.AddProfileAsync(Telegram, It.IsAny<JsonElement>())).ReturnsAsync(4);
        var fp = await this.FingerprintOf(DriftField.Profiles);

        var ex = await Assert.ThrowsAsync<PoracleRequestRefusedException>(() => this._sut.ResolveAsync(Pair(), DriftField.Profiles, LinkSide.Discord, fp));

        Assert.Equal(409, ex.StatusCode);
        Assert.Contains("Nothing was changed", ex.Message, StringComparison.Ordinal);
        this._human.Verify(p => p.DeleteProfileAsync(Telegram, 4), Times.Once);
    }

    [Fact]
    public async Task ResolveProfiles_ImpossibleNumberAfterAFiller_SaysSomeChangesWereMadeAndDeletesTheFiller()
    {
        this._data[Discord].Profiles = [new ProfileData(1, "Home"), new ProfileData(3, "Work")];
        this._data[Telegram].Profiles = [new ProfileData(1, "Home")];
        this._human.SetupSequence(p => p.AddProfileAsync(Telegram, It.IsAny<JsonElement>())).ReturnsAsync(2).ReturnsAsync(4);
        var fp = await this.FingerprintOf(DriftField.Profiles);

        var ex = await Assert.ThrowsAsync<DriftPartiallyAppliedException>(() => this._sut.ResolveAsync(Pair(), DriftField.Profiles, LinkSide.Discord, fp));

        Assert.Equal("Some changes were made before this failed. Look at the differences on Linked accounts again.", ex.Message);
        this._human.Verify(p => p.DeleteProfileAsync(Telegram, 4), Times.Once);
        this._human.Verify(p => p.DeleteProfileAsync(Telegram, 2), Times.Once);
    }

    [Fact]
    public async Task ResolveProfiles_FailureAfterAFiller_StillDeletesTheFiller()
    {
        this._data[Discord].Profiles = [new ProfileData(1, "Home"), new ProfileData(3, "Work")];
        this._data[Telegram].Profiles = [new ProfileData(1, "Home")];
        this._human.SetupSequence(p => p.AddProfileAsync(Telegram, It.IsAny<JsonElement>())).ReturnsAsync(2).ReturnsAsync(3);
        this._human.Setup(p => p.SetProfileGeographyAsync(Telegram, 3, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<double>(), It.IsAny<double>()))
            .ThrowsAsync(new HttpRequestException("down"));
        var fp = await this.FingerprintOf(DriftField.Profiles);

        await Assert.ThrowsAsync<DriftPartiallyAppliedException>(() => this._sut.ResolveAsync(Pair(), DriftField.Profiles, LinkSide.Discord, fp));

        this._human.Verify(p => p.DeleteProfileAsync(Telegram, 2), Times.Once);
    }

    [Fact]
    public async Task ResolvePlaces_PlaceUsedOnlyOnAnotherProfile_RefusedUpFront()
    {
        this._data[Discord].Places = [];
        this._data[Telegram].Places = [new SavedPlace { Label = "extra", Latitude = 1, Longitude = 1 }];
        this._data[Telegram].OtherProfilePlaceRefs = ["extra"];
        var fp = await this.FingerprintOf(DriftField.Places);

        var ex = await Assert.ThrowsAsync<DriftPlaceInUseException>(() => this._sut.ResolveAsync(Pair(), DriftField.Places, LinkSide.Discord, fp));

        Assert.Equal("extra", ex.Label);
        this._human.Verify(p => p.DeletePlaceAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ResolvePlaces_OtherPlaceReferenced_UnusedOneIsStillDeleted()
    {
        this._data[Discord].Places = [new SavedPlace { Label = "park", Latitude = 51.4, Longitude = -0.3 }];
        this._data[Telegram].Places = [new SavedPlace { Label = "park", Latitude = 51.4, Longitude = -0.3 }, new SavedPlace { Label = "extra", Latitude = 1, Longitude = 1 }];
        this._data[Telegram].OtherProfilePlaceRefs = ["park"];
        var fp = await this.FingerprintOf(DriftField.Places);

        await this._sut.ResolveAsync(Pair(), DriftField.Places, LinkSide.Discord, fp);

        this._human.Verify(p => p.DeletePlaceAsync(Telegram, "extra"), Times.Once);
    }

    [Fact]
    public async Task ResolveProfiles_FailureAfterAWrite_SaysSomeChangesWereMade()
    {
        this._data[Discord].Profiles = [new ProfileData(1, "Home"), new ProfileData(2, "Work")];
        this._data[Telegram].Profiles = [new ProfileData(1, "Home")];
        this._human.Setup(p => p.AddProfileAsync(Telegram, It.IsAny<JsonElement>())).ReturnsAsync(2);
        this._human.Setup(p => p.SetProfileGeographyAsync(Telegram, 2, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<double>(), It.IsAny<double>()))
            .ThrowsAsync(new InvalidOperationException("boom"));
        var fp = await this.FingerprintOf(DriftField.Profiles);

        var ex = await Assert.ThrowsAsync<DriftPartiallyAppliedException>(() => this._sut.ResolveAsync(Pair(), DriftField.Profiles, LinkSide.Discord, fp));

        Assert.Equal("Some changes were made before this failed. Look at the differences on Linked accounts again.", ex.Message);
        Assert.DoesNotContain("Nothing was changed", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolveProfiles_RefusableDeleteAlongsideCreate_WritesNothing()
    {
        this._data[Discord].Profiles = [new ProfileData(1, "Home"), new ProfileData(2, "Work")];
        this._data[Telegram].Profiles = [new ProfileData(1, "Home"), new ProfileData(3, "Spare")];
        this._data[Telegram].Rules = [3];
        var fp = await this.FingerprintOf(DriftField.Profiles);

        var ex = await Assert.ThrowsAsync<DriftProfileHoldsAlarmsException>(() => this._sut.ResolveAsync(Pair(), DriftField.Profiles, LinkSide.Discord, fp));

        Assert.Contains("Nothing was changed", ex.Message, StringComparison.Ordinal);
        this._human.Verify(p => p.AddProfileAsync(It.IsAny<string>(), It.IsAny<JsonElement>()), Times.Never);
    }

    [Fact]
    public async Task ResolveProfiles_DeletingTheOtherSidesCurrentProfile_SwitchesFirst()
    {
        this._data[Discord].Profiles = [new ProfileData(1, "Home")];
        this._data[Telegram].Profiles = [new ProfileData(1, "Home"), new ProfileData(2, "Spare")];
        this._data[Telegram].Current = 2;
        var order = new List<string>();
        this._human.Setup(p => p.SwitchProfileAsync(Telegram, 1)).Callback(() => order.Add("switch")).Returns(Task.CompletedTask);
        this._human.Setup(p => p.DeleteProfileAsync(Telegram, 2)).Callback(() => order.Add("delete")).Returns(Task.CompletedTask);
        var fp = await this.FingerprintOf(DriftField.Profiles);

        await this._sut.ResolveAsync(Pair(), DriftField.Profiles, LinkSide.Discord, fp);

        Assert.Equal(["switch", "delete"], order);
    }

    [Fact]
    public async Task ResolvePlaces_DeleteOfPlaceInUse_RefusedUpFront_NothingWritten()
    {
        this._data[Discord].Places = [new SavedPlace { Label = "park", Latitude = 51.4, Longitude = -0.3 }];
        this._data[Telegram].Places = [new SavedPlace { Label = "extra", Latitude = 1, Longitude = 1 }];
        this._data[Telegram].PlaceRefs = ["extra", "extra"];
        var fp = await this.FingerprintOf(DriftField.Places);

        var ex = await Assert.ThrowsAsync<DriftPlaceInUseException>(() => this._sut.ResolveAsync(Pair(), DriftField.Places, LinkSide.Discord, fp));

        Assert.Equal(2, ex.Count);
        Assert.Contains("extra", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Nothing was changed", ex.Message, StringComparison.Ordinal);
        this._human.Verify(p => p.AddPlaceAsync(It.IsAny<string>(), It.IsAny<SavedPlace>()), Times.Never);
        this._human.Verify(p => p.DeletePlaceAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ResolvePlaces_InUseAppearsMidRun_BecomesDriftRefusalWhenNothingWritten()
    {
        this._data[Discord].Places = [];
        this._data[Telegram].Places = [new SavedPlace { Label = "extra", Latitude = 1, Longitude = 1 }];
        this._human.Setup(p => p.DeletePlaceAsync(Telegram, "extra")).ThrowsAsync(new PlaceInUseException(["a", "b", "c"]));
        var fp = await this.FingerprintOf(DriftField.Places);

        var ex = await Assert.ThrowsAsync<DriftPlaceInUseException>(() => this._sut.ResolveAsync(Pair(), DriftField.Places, LinkSide.Discord, fp));

        Assert.Equal(3, ex.Count);
    }

    [Fact]
    public async Task ResolvePlaces_InUseAppearsAfterAWrite_SaysSomeChangesWereMade()
    {
        this._data[Discord].Places = [new SavedPlace { Label = "park", Latitude = 51.4, Longitude = -0.3 }];
        this._data[Telegram].Places = [new SavedPlace { Label = "extra", Latitude = 1, Longitude = 1 }];
        this._human.Setup(p => p.DeletePlaceAsync(Telegram, "extra")).ThrowsAsync(new PlaceInUseException(["a"]));
        var fp = await this.FingerprintOf(DriftField.Places);

        await Assert.ThrowsAsync<DriftPartiallyAppliedException>(() => this._sut.ResolveAsync(Pair(), DriftField.Places, LinkSide.Discord, fp));
    }

    [Fact]
    public async Task ResolvePlaces_RefusedAdd_Is400WithItsReason()
    {
        this._data[Discord].Places = [new SavedPlace { Label = "park", Latitude = 51.4, Longitude = -0.3 }];
        this._data[Telegram].Places = [];
        this._human.Setup(p => p.AddPlaceAsync(Telegram, It.IsAny<SavedPlace>())).ReturnsAsync("label already used");
        var fp = await this.FingerprintOf(DriftField.Places);

        var ex = await Assert.ThrowsAsync<PoracleRequestRefusedException>(() => this._sut.ResolveAsync(Pair(), DriftField.Places, LinkSide.Discord, fp));

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal("label already used", ex.Message);
    }

    [Fact]
    public async Task PlaceLabelsDifferingOnlyByCase_AreTheSamePlace()
    {
        this._data[Discord].Places = [new SavedPlace { Label = "Gym", Latitude = 51.5, Longitude = -0.1 }];
        this._data[Telegram].Places = [new SavedPlace { Label = "gym", Latitude = 51.5, Longitude = -0.1 }];

        Assert.Empty(await this._sut.GetAsync(Pair()));
    }

    [Fact]
    public async Task ResolvePlaces_CaseDifferentLabel_UpdatesUnderTheOtherSidesStoredLabel()
    {
        this._data[Discord].Places = [new SavedPlace { Label = "Gym", Latitude = 51.5, Longitude = -0.1 }];
        this._data[Telegram].Places = [new SavedPlace { Label = "gym", Latitude = 51.9, Longitude = -0.1 }];
        var fp = await this.FingerprintOf(DriftField.Places);

        await this._sut.ResolveAsync(Pair(), DriftField.Places, LinkSide.Discord, fp);

        this._human.Verify(p => p.UpdatePlaceAsync(Telegram, "gym", 51.5, -0.1), Times.Once);
        this._human.Verify(p => p.AddPlaceAsync(It.IsAny<string>(), It.IsAny<SavedPlace>()), Times.Never);
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

    [Fact]
    public async Task ProfileZeroOnOneSide_IdenticalProfileOne_NoProfilesDrift()
    {
        this._data[Discord].Profiles = [new ProfileData(0, "Old"), new ProfileData(1, "Home")];
        this._data[Telegram].Profiles = [new ProfileData(1, "Home")];

        Assert.Empty(await this._sut.GetAsync(Pair()));
    }

    [Fact]
    public async Task ProfileZeroOnOneSide_DifferentProfileOne_StillDrifts_AndResolveRenamesOnlyProfileOne()
    {
        this._data[Discord].Profiles = [new ProfileData(0, "Old"), new ProfileData(1, "Home")];
        this._data[Telegram].Profiles = [new ProfileData(1, "Casa")];
        var fp = await this.FingerprintOf(DriftField.Profiles);

        await this._sut.ResolveAsync(Pair(), DriftField.Profiles, LinkSide.Discord, fp);

        this._human.Verify(p => p.UpdateProfileAsync(Telegram, It.Is<JsonElement>(b => b.GetProperty("profile_no").GetInt32() == 1 && b.GetProperty("name").GetString() == "Home")), Times.Once);
        this._human.Verify(p => p.UpdateProfileAsync(It.IsAny<string>(), It.Is<JsonElement>(b => b.GetProperty("profile_no").GetInt32() == 0)), Times.Never);
        this._human.Verify(p => p.AddProfileAsync(It.IsAny<string>(), It.IsAny<JsonElement>()), Times.Never);
        this._human.Verify(p => p.DeleteProfileAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task ProfileZeroOnTheOtherSide_IsNeverDeleted()
    {
        this._data[Discord].Profiles = [new ProfileData(1, "Home")];
        this._data[Telegram].Profiles = [new ProfileData(0, "Old"), new ProfileData(1, "Casa")];
        this._data[Telegram].Rules = [0];
        var fp = await this.FingerprintOf(DriftField.Profiles);

        await this._sut.ResolveAsync(Pair(), DriftField.Profiles, LinkSide.Discord, fp);

        this._human.Verify(p => p.DeleteProfileAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task CurrentProfileZeroOnOneSide_NoCurrentProfileDrift()
    {
        this._data[Telegram].Profiles = [new ProfileData(0, "Old"), new ProfileData(1, "Home")];
        this._data[Telegram].Current = 0;

        Assert.Empty(await this._sut.GetAsync(Pair()));
    }

    [Fact]
    public async Task ResolveProfiles_KeptSideOnProfileZero_SwitchesTheOtherToItsLowestRealProfile()
    {
        this._data[Discord].Profiles = [new ProfileData(0, "Old"), new ProfileData(1, "Home")];
        this._data[Discord].Current = 0;
        this._data[Telegram].Profiles = [new ProfileData(1, "Home"), new ProfileData(2, "Spare")];
        this._data[Telegram].Current = 2;
        var fp = await this.FingerprintOf(DriftField.Profiles);

        await this._sut.ResolveAsync(Pair(), DriftField.Profiles, LinkSide.Discord, fp);

        this._human.Verify(p => p.SwitchProfileAsync(Telegram, 1), Times.Once);
        this._human.Verify(p => p.SwitchProfileAsync(It.IsAny<string>(), 0), Times.Never);
    }

    [Fact]
    public async Task ResolveProfiles_LegitimateGap_StillCreatesProfileTwo()
    {
        this._data[Discord].Profiles = [new ProfileData(1, "Home"), new ProfileData(2, "Work")];
        this._data[Telegram].Profiles = [new ProfileData(1, "Home")];
        this._human.Setup(p => p.AddProfileAsync(Telegram, It.IsAny<JsonElement>())).ReturnsAsync(2);

        var item = Assert.Single(await this._sut.GetAsync(Pair()));
        await this._sut.ResolveAsync(Pair(), DriftField.Profiles, LinkSide.Discord, item.Fingerprint);

        Assert.Equal(DriftField.Profiles, item.Field);
        this._human.Verify(p => p.AddProfileAsync(Telegram, It.Is<JsonElement>(b => b.GetProperty("name").GetString() == "Work")), Times.Once);
    }

    [Fact]
    public async Task LegacyProfileZero_NamesTheSideThatHasItAlone()
    {
        this._data[Discord].Profiles = [new ProfileData(0, "Old"), new ProfileData(1, "Home")];
        Assert.Equal(LinkSide.Discord, await this._sut.GetLegacyProfileZeroAsync(Pair()));

        this._data[Discord].Profiles = [new ProfileData(1, "Home")];
        this._data[Telegram].Profiles = [new ProfileData(0, "Old"), new ProfileData(1, "Home")];
        Assert.Equal(LinkSide.Telegram, await this._sut.GetLegacyProfileZeroAsync(Pair()));
    }

    [Fact]
    public async Task LegacyProfileZero_NullWhenNeitherBothOrUnlinked()
    {
        Assert.Null(await this._sut.GetLegacyProfileZeroAsync(Pair()));

        this._data[Discord].Profiles = [new ProfileData(0, "Old"), new ProfileData(1, "Home")];
        this._data[Telegram].Profiles = [new ProfileData(0, "Old"), new ProfileData(1, "Home")];
        Assert.Null(await this._sut.GetLegacyProfileZeroAsync(Pair()));

        this._human.Invocations.Clear();
        Assert.Null(await this._sut.GetLegacyProfileZeroAsync(LinkedPair.Single(Discord)));
        this._human.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task LocationDiffers_ActiveProfileMirrorsIt_OnlyTheLocationLine()
    {
        // PoracleNG keeps the active profile's row in step with the human, so one move shows in both places.
        this._data[Telegram].Lat = 51.6;
        this._data[Telegram].Profiles = [new ProfileData(1, "Home", Lat: 51.6)];

        var item = Assert.Single(await this._sut.GetAsync(Pair()));

        Assert.Equal(DriftField.Location, item.Field);
    }

    [Fact]
    public async Task AreasDiffer_ActiveProfileMirrorsThem_OnlyTheAreasLine()
    {
        this._data[Telegram].Areas = ["riverside"];
        this._data[Telegram].Profiles = [new ProfileData(1, "Home", Area: "[\"riverside\"]")];

        var item = Assert.Single(await this._sut.GetAsync(Pair()));

        Assert.Equal(DriftField.Areas, item.Field);
    }

    [Fact]
    public async Task AnotherProfilesAreasDiffer_StillAProfilesLine()
    {
        this._data[Discord].Profiles = [new ProfileData(1, "Home"), new ProfileData(2, "Work", Area: "[\"old town\"]")];
        this._data[Telegram].Profiles = [new ProfileData(1, "Home"), new ProfileData(2, "Work", Area: "[\"riverside\"]")];

        var item = Assert.Single(await this._sut.GetAsync(Pair()));

        Assert.Equal(DriftField.Profiles, item.Field);
    }

    [Fact]
    public async Task ResolveProfiles_LeavesTheSharedActiveProfilesGeographyToItsOwnLines()
    {
        this._data[Discord].Profiles = [new ProfileData(1, "Home", Area: "[\"old town\"]"), new ProfileData(2, "Work")];
        this._data[Telegram].Profiles = [new ProfileData(1, "Home", Area: "[\"riverside\"]"), new ProfileData(2, "Office")];
        var fp = await this.FingerprintOf(DriftField.Profiles);

        await this._sut.ResolveAsync(Pair(), DriftField.Profiles, LinkSide.Discord, fp);

        this._human.Verify(p => p.UpdateProfileAsync(Telegram, It.IsAny<JsonElement>()), Times.Once);
        this._human.Verify(p => p.SetProfileGeographyAsync(Telegram, 1, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<double>(), It.IsAny<double>()), Times.Never);
    }

    [Fact]
    public async Task ProfilesLineComesBeforeTheActiveProfileLine()
    {
        this._data[Discord].Profiles = [new ProfileData(1, "Home"), new ProfileData(2, "Work")];
        this._data[Telegram].Profiles = [new ProfileData(1, "Home")];
        this._data[Discord].Current = 2;

        var fields = (await this._sut.GetAsync(Pair())).Select(i => i.Field).ToList();

        Assert.Equal([DriftField.Profiles, DriftField.CurrentProfile], fields);
    }

    [Fact]
    public async Task ActiveProfileLine_CarriesNamesAndWhetherTheOtherSideHasIt()
    {
        this._data[Discord].Profiles = [new ProfileData(1, "Home"), new ProfileData(2, "Weekend")];
        this._data[Telegram].Profiles = [new ProfileData(1, "Home")];
        this._data[Discord].Current = 2;

        var item = (await this._sut.GetAsync(Pair())).Single(i => i.Field == DriftField.CurrentProfile);

        var details = item.Details!.Value;
        Assert.Equal("Weekend", details.GetProperty("discord").GetProperty("name").GetString());
        Assert.False(details.GetProperty("discord").GetProperty("otherHas").GetBoolean());
        Assert.Equal("Home", details.GetProperty("telegram").GetProperty("name").GetString());
        Assert.True(details.GetProperty("telegram").GetProperty("otherHas").GetBoolean());
    }

    [Fact]
    public async Task ActiveProfileDetails_AreOutsideTheFingerprint()
    {
        this._data[Discord].Profiles = [new ProfileData(1, "Home"), new ProfileData(2, "Work")];
        this._data[Telegram].Profiles = [new ProfileData(1, "Home"), new ProfileData(2, "Work")];
        this._data[Discord].Current = 2;
        var before = await this.FingerprintOf(DriftField.CurrentProfile);

        this._data[Discord].Profiles = [new ProfileData(1, "Home"), new ProfileData(2, "Office")];

        Assert.Equal(before, await this.FingerprintOf(DriftField.CurrentProfile));
    }

    [Fact]
    public async Task ResolveCurrentProfile_OtherSideLacksTheProfile_RefusesNamingItAndSwitchesNothing()
    {
        this._data[Discord].Profiles = [new ProfileData(1, "Home"), new ProfileData(2, "Weekend")];
        this._data[Telegram].Profiles = [new ProfileData(1, "Home")];
        this._data[Discord].Current = 2;
        var fp = await this.FingerprintOf(DriftField.CurrentProfile);

        var ex = await Assert.ThrowsAsync<DriftProfileMissingException>(() => this._sut.ResolveAsync(Pair(), DriftField.CurrentProfile, LinkSide.Discord, fp));

        Assert.Equal(LinkSide.Telegram, ex.Side);
        Assert.Equal("Weekend", ex.Name);
        this._human.Verify(p => p.SwitchProfileAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task ResolveCurrentProfile_KeepingTheSideWhoseProfileBothHave_StillSwitches()
    {
        this._data[Discord].Profiles = [new ProfileData(1, "Home"), new ProfileData(2, "Weekend")];
        this._data[Telegram].Profiles = [new ProfileData(1, "Home")];
        this._data[Discord].Current = 2;
        var fp = await this.FingerprintOf(DriftField.CurrentProfile);

        await this._sut.ResolveAsync(Pair(), DriftField.CurrentProfile, LinkSide.Telegram, fp);

        this._human.Verify(p => p.SwitchProfileAsync(Discord, 1), Times.Once);
    }

    [Fact]
    public async Task PlacesLine_CarriesTheLabelsAsTyped()
    {
        this._data[Discord].Places = [new SavedPlace { Label = "Mum's House", Latitude = 51.4, Longitude = -0.3 }];
        this._data[Telegram].Places = [new SavedPlace { Label = "ACLT Gym", Latitude = 51.2, Longitude = -0.1 }];

        var item = Assert.Single(await this._sut.GetAsync(Pair()));

        var labels = item.Details!.Value.GetProperty("labels");
        Assert.Equal("Mum's House", labels.GetProperty("mum's house").GetString());
        Assert.Equal("ACLT Gym", labels.GetProperty("aclt gym").GetString());
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
        public List<string> PlaceRefs { get; set; } = [];
        public List<string> OtherProfilePlaceRefs { get; set; } = [];
        public List<ProfileData> Profiles { get; set; } = [new ProfileData(1, "Home")];

        public string ProfilesJson() => JsonSerializer.Serialize(new
        {
            profile = this.Profiles.Select(p => new { profile_no = p.No, name = p.Name, area = p.Area, latitude = p.Lat, longitude = p.Lon, active_hours = p.Hours }),
        });
    }
}
