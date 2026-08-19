using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// A write must not erase what PoracleWeb cannot see.
/// <para>
/// The bulk and edit paths built their body by serializing the typed alarm model, so any column PoracleNG
/// grew that PoracleWeb never modelled was absent from the write. Because the POST carries a uid,
/// PoracleNG upserted the row and stored the column default over the user's value. PoracleNG 5.1.0 added
/// <c>override_location_label</c>, <c>override_areas</c> and <c>pvp_ranking_evolution</c>; 5.2.0 adds
/// <c>costume</c>. Set one with the bot, press Update Distance on the web, and it was gone. See #730.
/// </para>
/// <para>
/// The fields asserted here are the ones PoracleWeb still does not model — <c>rarity</c>, which it
/// deliberately does not offer, and <c>costume</c>, which 5.2.0 has not shipped yet. Once a field is
/// modelled its value comes from the caller, which is a different guarantee: see
/// <see cref="PokemonFieldCoverageTests"/>.
/// </para>
/// <para>
/// These assert the legitimate case still works (the distance genuinely changes, the count is still
/// reported) alongside the preservation, because a helper that dropped the change on the floor would
/// preserve everything perfectly and do nothing useful.
/// </para>
/// </summary>
public class UnmodelledFieldPreservationTests
{
    private readonly Mock<IPoracleTrackingProxy> _proxy = new();
    private readonly Mock<IFeatureGate> _featureGate = new();
    private readonly Mock<ITrackedUidRemapper> _remapper = new();
    private readonly List<JsonElement> _sent = [];

    /// <summary>A stored row as PoracleNG 5.1.0 returns it, carrying fields PoracleWeb has no model for.</summary>
    private const string StoredRow =
        "[{"
        + "\"uid\": 7,"
        + "\"id\": \"u1\","
        + "\"profile_no\": 0,"
        + "\"pokemon_id\": 201,"
        + "\"distance\": 500,"
        + "\"clean\": 0,"
        + "\"template\": \"1\","
        + "\"level\": 5,"
        + "\"grunt_type\": \"blanche\","
        + "\"gender\": 0,"
        + "\"lure_id\": 501,"
        + "\"override_location_label\": \"work\","
        + "\"override_areas\": [\"terrigal\"],"
        + "\"pvp_ranking_evolution\": 2,"
        + "\"rarity\": 3,"
        + "\"costume\": 9000"
        + "}]";

    public UnmodelledFieldPreservationTests()
    {
        this._featureGate.Setup(g => g.EnsureEnabledAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        this._remapper
            .Setup(r => r.RemapAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns(Task.CompletedTask);
        this._proxy
            .Setup(p => p.GetByUserAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(() => JsonDocument.Parse(StoredRow).RootElement.Clone());
        this._proxy
            .Setup(p => p.CreateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<JsonElement>()))
            .Callback<string, string, JsonElement>((_, _, body) => this._sent.Add(body.Clone()))
            .ReturnsAsync(new TrackingCreateResult([7], 0, 0, 1));
        this._proxy
            .Setup(p => p.DeleteByUidAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
            .Returns(Task.CompletedTask);
        this._proxy
            .Setup(p => p.BulkDeleteByUidsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<List<int>>()))
            .Returns(Task.CompletedTask);
    }

    /// <summary>Every alarm type's bulk distance write, by the name PoracleNG knows it by.</summary>
    public static TheoryData<string> AllTrackingTypes() =>
    [
        "pokemon", "raid", "egg", "quest", "invasion", "lure", "nest", "gym", "fort", "maxbattle",
    ];

    [Theory]
    [MemberData(nameof(AllTrackingTypes))]
    public async Task BulkDistanceForEveryAlarmKeepsUnmodelledFields(string trackingType)
    {
        var changed = await UpdateAllDistance(this.ServiceFor(trackingType), 1500);

        Assert.Equal(1, changed);
        var row = this.OnlyRowSent();
        Assert.Equal(1500, row.GetProperty("distance").GetInt32());
        AssertCarriedForward(row);
    }

    [Theory]
    [MemberData(nameof(AllTrackingTypes))]
    public async Task BulkDistanceForSelectedAlarmsKeepsUnmodelledFields(string trackingType)
    {
        var changed = await UpdateSelectedDistance(this.ServiceFor(trackingType), 1500);

        Assert.Equal(1, changed);
        var row = this.OnlyRowSent();
        Assert.Equal(1500, row.GetProperty("distance").GetInt32());
        AssertCarriedForward(row);
    }

    [Theory]
    [MemberData(nameof(AllTrackingTypes))]
    public async Task BulkDistanceRewritesOnlyTheSelectedRow(string trackingType)
    {
        // The legitimate-case half: selection still has to work now that the rewrite runs off the stored
        // rows rather than a filtered typed list.
        await UpdateSelectedDistance(this.ServiceFor(trackingType), 1500);

        Assert.Equal(7, this.OnlyRowSent().GetProperty("uid").GetInt32());
    }

    [Theory]
    [MemberData(nameof(AllTrackingTypes))]
    public async Task BulkDistanceStillStripsProfileNo(string trackingType)
    {
        // The stored row carries profile_no and the rewrite passes properties through verbatim, so the
        // strip has to survive the new path or #411 comes straight back.
        await UpdateAllDistance(this.ServiceFor(trackingType), 1500);

        Assert.False(this.OnlyRowSent().TryGetProperty("profile_no", out _));
    }

    [Fact]
    public async Task BulkDistanceOnNoAlarmsWritesNothing()
    {
        this._proxy
            .Setup(p => p.GetByUserAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(JsonDocument.Parse("[]").RootElement.Clone());

        Assert.Equal(0, await UpdateAllDistance(this.ServiceFor("pokemon"), 1500));
        Assert.Empty(this._sent);
    }

    // The four edit tests below are the four distinct shapes UpdateAsync takes. Pokemon writes straight
    // through; raid, egg, quest, nest, gym and fort share the reconciler shape verbatim; invasion and
    // lure share the natural-key replace; maxbattle deletes then re-creates. The preservation call is
    // the same line in all ten, so one test per shape covers the wiring without ten near-identical
    // fixtures, each of which would have to satisfy that type's own natural-key guard.

    [Fact]
    public async Task EditKeepsUnmodelledFieldsOnPokemon()
    {
        var service = new MonsterService(this._proxy.Object, this._featureGate.Object);

        await service.UpdateAsync("u1", new Monster { Uid = 7, PokemonId = 201, Distance = 1500 });

        var row = this.OnlyRowSent();
        Assert.Equal(1500, row.GetProperty("distance").GetInt32());
        AssertCarriedForward(row);
    }

    [Fact]
    public async Task EditKeepsUnmodelledFieldsOnRaid()
    {
        var service = new RaidService(
            this._proxy.Object, this._featureGate.Object, NullLogger<RaidService>.Instance, this._remapper.Object);

        await service.UpdateAsync("u1", new Raid { Uid = 7, PokemonId = 9000, Level = 5, Distance = 1500 });

        AssertCarriedForward(this.OnlyRowSent());
    }

    [Fact]
    public async Task EditKeepsUnmodelledFieldsOnInvasion()
    {
        var service = new InvasionService(
            this._proxy.Object, this._featureGate.Object, NullLogger<InvasionService>.Instance, this._remapper.Object);

        await service.UpdateAsync("u1", new Invasion { Uid = 7, GruntType = "blanche", Gender = 0, Distance = 1500 });

        Assert.Contains(
            this._sent,
            b => b.ValueKind == JsonValueKind.Object
                && b.TryGetProperty("override_location_label", out var label)
                && label.GetString() == "work");
    }

    [Fact]
    public async Task EditKeepsUnmodelledFieldsOnMaxBattle()
    {
        var service = new MaxBattleService(
            this._proxy.Object, this._featureGate.Object, NullLogger<MaxBattleService>.Instance, this._remapper.Object);

        await service.UpdateAsync("u1", new MaxBattle { Uid = 7, PokemonId = 201, Distance = 1500 });

        AssertCarriedForward(this.OnlyRowSent());
    }

    [Fact]
    public async Task AnEmptyOverrideClearsItRatherThanBeingCarriedForward()
    {
        // The other half of the null rule. Null means "not stated, keep what is stored"; empty is how a
        // person says "remove it". Without this, an override could be set but never taken off.
        var service = new MonsterService(this._proxy.Object, this._featureGate.Object);

        await service.UpdateAsync("u1", new Monster
        {
            Uid = 7,
            PokemonId = 201,
            Distance = 1500,
            OverrideLocationLabel = string.Empty,
            OverrideAreas = [],
        });

        var row = this.OnlyRowSent();
        Assert.Equal(string.Empty, row.GetProperty("override_location_label").GetString());
        Assert.Empty(row.GetProperty("override_areas").EnumerateArray());
    }

    [Fact]
    public async Task CreateDoesNotInventFieldsFromAnotherAlarm()
    {
        // uid 0 is a create. There is no stored row to carry anything forward from, and matching on
        // "some row the user already has" would staple a stranger's location override onto a new alarm.
        var service = new MonsterService(this._proxy.Object, this._featureGate.Object);

        await service.CreateAsync("u1", new Monster { PokemonId = 999, Distance = 1500 });

        // The model states no override, so the write carries an explicit null rather than the "work"
        // some other alarm of theirs holds.
        Assert.Equal(
            JsonValueKind.Null,
            this.OnlyRowSent().GetProperty("override_location_label").ValueKind);
    }

    private static void AssertCarriedForward(JsonElement row)
    {
        Assert.Equal("work", row.GetProperty("override_location_label").GetString());
        Assert.Equal("terrigal", row.GetProperty("override_areas").EnumerateArray().Single().GetString());
        Assert.Equal(3, row.GetProperty("rarity").GetInt32());
        Assert.Equal(9000, row.GetProperty("costume").GetInt32());
    }

    private static Task<int> UpdateAllDistance(object service, int distance) => service switch
    {
        IMonsterService s => s.UpdateDistanceByUserAsync("u1", 0, distance),
        IRaidService s => s.UpdateDistanceByUserAsync("u1", 0, distance),
        IEggService s => s.UpdateDistanceByUserAsync("u1", 0, distance),
        IQuestService s => s.UpdateDistanceByUserAsync("u1", 0, distance),
        IInvasionService s => s.UpdateDistanceByUserAsync("u1", 0, distance),
        ILureService s => s.UpdateDistanceByUserAsync("u1", 0, distance),
        INestService s => s.UpdateDistanceByUserAsync("u1", 0, distance),
        IGymService s => s.UpdateDistanceByUserAsync("u1", 0, distance),
        IFortChangeService s => s.UpdateDistanceByUserAsync("u1", 0, distance),
        IMaxBattleService s => s.UpdateDistanceByUserAsync("u1", 0, distance),
        _ => throw new ArgumentOutOfRangeException(nameof(service)),
    };

    private static Task<int> UpdateSelectedDistance(object service, int distance) => service switch
    {
        IMonsterService s => s.UpdateDistanceByUidsAsync([7], "u1", distance),
        IRaidService s => s.UpdateDistanceByUidsAsync([7], "u1", distance),
        IEggService s => s.UpdateDistanceByUidsAsync([7], "u1", distance),
        IQuestService s => s.UpdateDistanceByUidsAsync([7], "u1", distance),
        IInvasionService s => s.UpdateDistanceByUidsAsync([7], "u1", distance),
        ILureService s => s.UpdateDistanceByUidsAsync([7], "u1", distance),
        INestService s => s.UpdateDistanceByUidsAsync([7], "u1", distance),
        IGymService s => s.UpdateDistanceByUidsAsync([7], "u1", distance),
        IFortChangeService s => s.UpdateDistanceByUidsAsync([7], "u1", distance),
        IMaxBattleService s => s.UpdateDistanceByUidsAsync([7], "u1", distance),
        _ => throw new ArgumentOutOfRangeException(nameof(service)),
    };

    private JsonElement OnlyRowSent()
    {
        var body = Assert.Single(this._sent);
        return body.ValueKind == JsonValueKind.Array ? body.EnumerateArray().Single() : body;
    }

    private object ServiceFor(string trackingType) => trackingType switch
    {
        "pokemon" => new MonsterService(this._proxy.Object, this._featureGate.Object),
        "raid" => new RaidService(
            this._proxy.Object, this._featureGate.Object, NullLogger<RaidService>.Instance, this._remapper.Object),
        "egg" => new EggService(
            this._proxy.Object, this._featureGate.Object, NullLogger<EggService>.Instance, this._remapper.Object),
        "quest" => new QuestService(
            this._proxy.Object, this._featureGate.Object, NullLogger<QuestService>.Instance, this._remapper.Object),
        "invasion" => new InvasionService(
            this._proxy.Object, this._featureGate.Object, NullLogger<InvasionService>.Instance, this._remapper.Object),
        "lure" => new LureService(
            this._proxy.Object, this._featureGate.Object, NullLogger<LureService>.Instance, this._remapper.Object),
        "nest" => new NestService(
            this._proxy.Object, this._featureGate.Object, NullLogger<NestService>.Instance, this._remapper.Object),
        "gym" => new GymService(
            this._proxy.Object, this._featureGate.Object, NullLogger<GymService>.Instance, this._remapper.Object),
        "fort" => new FortChangeService(
            this._proxy.Object, this._featureGate.Object, NullLogger<FortChangeService>.Instance, this._remapper.Object),
        "maxbattle" => new MaxBattleService(
            this._proxy.Object, this._featureGate.Object, NullLogger<MaxBattleService>.Instance, this._remapper.Object),
        _ => throw new ArgumentOutOfRangeException(nameof(trackingType), trackingType, "unknown tracking type"),
    };
}
