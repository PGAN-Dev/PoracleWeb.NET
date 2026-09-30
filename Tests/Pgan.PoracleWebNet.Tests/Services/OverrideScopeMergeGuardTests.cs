using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Mappings;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;
using Pgan.PoracleWebNet.Tests.TestDoubles;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// The merge guard has to recognise "no override" however it is spelled.
/// </summary>
/// <remarks>
/// <para>
/// Every add and edit dialog sends the unused half of a scope as an explicit empty --
/// <c>"overrideAreas": []</c>, <c>"overrideLocationLabel": ""</c> -- because null means "keep what is
/// stored" on the write path (<c>alarm-scope.ts</c>, <c>scopeToFields</c>). PoracleNG stores no override
/// as NULL and reads it back as <c>override_areas: null</c>, <c>override_location_label: ""</c>, and its
/// own diff treats the two spellings as the same value. Verified on 5.1.0 and 5.2.1: posting
/// <c>{pokemon_id:4, min_iv:50, distance:2000, override_areas:[], override_location_label:""}</c> over a
/// stored rule at distance 1000 answers <c>{"updates":1}</c> and the original rule is gone.
/// </para>
/// <para>
/// The guard compared <c>[]</c> against <c>null</c> as an identity difference, so it never fired for an
/// Add made through the UI, and PoracleWeb.NET answered 201 with the uid of the alarm it had just
/// overwritten. These tests post the body the SPA builds, bound through the <c>*Create</c> DTO the
/// controller binds, against stored rows in the shape PoracleNG returns them.
/// </para>
/// </remarks>
public class OverrideScopeMergeGuardTests
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    private readonly Mock<IPoracleTrackingProxy> _proxy = new();
    private readonly Mock<IFeatureGate> _gate = new();
    private readonly Mock<ITrackedUidRemapper> _remapper = new();

    public OverrideScopeMergeGuardTests()
    {
        this._gate.Setup(g => g.EnsureEnabledAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        this._proxy
            .Setup(p => p.CreateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([77], 0, 0, 1));
    }

    /// <summary>The scope fields every dialog appends for "no override, radius from my pin".</summary>
    private const string NoOverride = "\"overrideAreas\":[],\"overrideLocationLabel\":\"\"";

    /// <summary>What each add dialog posts, trimmed to the fields that decide identity, at 2 km.</summary>
    public static TheoryData<string, string> SpaCreateBodies() => new()
    {
        { "pokemon", "{\"pokemonId\":4,\"form\":0,\"minIv\":50,\"maxIv\":100,\"distance\":2000,\"clean\":0," + NoOverride + "}" },
        { "raid", "{\"pokemonId\":150,\"level\":9000,\"team\":4,\"exclusive\":0,\"form\":0,\"distance\":2000,\"clean\":0,\"gymId\":null," + NoOverride + "}" },
        { "egg", "{\"level\":5,\"team\":4,\"exclusive\":0,\"distance\":2000,\"clean\":0,\"gymId\":null," + NoOverride + "}" },
        { "quest", "{\"rewardType\":7,\"reward\":25,\"distance\":2000,\"clean\":0," + NoOverride + "}" },
        { "nest", "{\"pokemonId\":25,\"minSpawnAvg\":0,\"distance\":2000,\"clean\":0," + NoOverride + "}" },
        { "gym", "{\"team\":4,\"slotChanges\":0,\"battleChanges\":0,\"distance\":2000,\"clean\":0,\"gymId\":null," + NoOverride + "}" },
        { "fort", "{\"fortType\":\"pokestop\",\"changeTypes\":[\"name\"],\"includeEmpty\":0,\"distance\":2000," + NoOverride + "}" },
    };

    [Theory]
    [MemberData(nameof(SpaCreateBodies))]
    public async Task AnAddFromTheUiThatWouldTakeOverAnExistingAlarmIsRefused(string type, string spaBody)
    {
        // The alarm the user already has: identical but for its radius, stored with no override.
        this.Store(type, StoredTwinOf(type, spaBody, uid: 50, distance: 1000));

        await Assert.ThrowsAsync<TrackingConflictException>(() => this.CreateAsync(type, spaBody));

        this._proxy.Verify(
            p => p.CreateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<JsonElement>()), Times.Never);
    }

    [Theory]
    [MemberData(nameof(SpaCreateBodies))]
    public async Task AnAddBesideAnAreaScopedTwinStillGoesThrough(string type, string spaBody)
    {
        // The legitimate case. A stored alarm limited to an area is a different alarm upstream -- the
        // override is an identity field -- so an unscoped Add beside it is an insert, not a takeover.
        var twin = StoredTwinOf(type, spaBody, uid: 50, distance: 0);
        twin["override_areas"] = new JsonArray("aberdeen");
        this.Store(type, twin);

        await this.CreateAsync(type, spaBody);

        this._proxy.Verify(p => p.CreateAsync(type, "u1", It.IsAny<JsonElement>()), Times.Once);
    }

    [Theory]
    [MemberData(nameof(SpaCreateBodies))]
    public async Task AnAddThatDiffersByTwoUpdatableFieldsStillGoesThrough(string type, string spaBody)
    {
        // Two updatable differences coexist upstream (#553), however the override is spelled.
        var twin = StoredTwinOf(type, spaBody, uid: 50, distance: 1000);
        twin["template"] = "custom";
        this.Store(type, twin);

        await this.CreateAsync(type, spaBody);

        this._proxy.Verify(p => p.CreateAsync(type, "u1", It.IsAny<JsonElement>()), Times.Once);
    }

    [Fact]
    public async Task TheLiveRowFromPoracleNg521IsRecognisedAsTheSameAlarm()
    {
        // Verbatim from GET /api/tracking/pokemon/{id} on 5.2.1, the version production runs.
        const string live = """
            [{"uid":37178,"id":"u1","profile_no":1,"ping":"","clean":0,"distance":1000,"template":"1","pokemon_id":4,
              "form":0,"costume":9000,"min_iv":50,"max_iv":100,"min_cp":0,"max_cp":9000,"min_level":0,"max_level":55,
              "atk":0,"def":0,"sta":0,"max_atk":15,"max_def":15,"max_sta":15,"gender":0,"min_weight":0,
              "max_weight":9000000,"min_time":0,"rarity":-1,"max_rarity":6,"size":-1,"max_size":5,
              "pvp_ranking_league":0,"pvp_ranking_best":1,"pvp_ranking_worst":4096,"pvp_ranking_min_cp":0,
              "pvp_ranking_cap":0,"pvp_ranking_evolution":0,"override_location_label":"","override_areas":null,
              "description":"**Charmander**  | distance: 1000m"}]
            """;
        this._proxy.Setup(p => p.GetByUserAsync("pokemon", "u1"))
            .ReturnsAsync(JsonDocument.Parse(live).RootElement.Clone());

        // The exact request from the defect report.
        await Assert.ThrowsAsync<TrackingConflictException>(() => this.CreateAsync(
            "pokemon",
            "{\"pokemonId\":4,\"minIv\":50,\"distance\":2000,\"overrideAreas\":[],\"overrideLocationLabel\":\"\"}"));
    }

    [Fact]
    public async Task AreaNamesAreComparedAsPoracleNgStoresThemLowercased()
    {
        // PoracleNG lowercases override_areas on the way in ("Academia" is stored as "academia"), and
        // posting ["Aberdeen"] over a stored ["aberdeen"] answers alreadyPresent. So a submission that
        // differs from a stored rule only by an area's case and by one updatable field is a takeover.
        var body = "{\"pokemonId\":4,\"minIv\":50,\"distance\":0,\"clean\":1,\"overrideAreas\":[\"Aberdeen\"],\"overrideLocationLabel\":\"\"}";
        var twin = StoredTwinOf("pokemon", body, uid: 50, distance: 0);
        twin["clean"] = 0;
        twin["override_areas"] = new JsonArray("aberdeen");
        this.Store("pokemon", twin);

        await Assert.ThrowsAsync<TrackingConflictException>(() => this.CreateAsync("pokemon", body));
    }

    [Fact]
    public async Task AreaOrderStillDistinguishesTwoAlarms()
    {
        // Verified on 5.2.1: ["aberdeen","academia"] and ["academia","aberdeen"] are stored as two rules.
        // Treating them as one would refuse a legitimate Add.
        var body = "{\"pokemonId\":4,\"minIv\":50,\"distance\":0,\"clean\":1,\"overrideAreas\":[\"academia\",\"aberdeen\"],\"overrideLocationLabel\":\"\"}";
        var twin = StoredTwinOf("pokemon", body, uid: 50, distance: 0);
        twin["clean"] = 0;
        twin["override_areas"] = new JsonArray("aberdeen", "academia");
        this.Store("pokemon", twin);

        await this.CreateAsync("pokemon", body);

        this._proxy.Verify(p => p.CreateAsync("pokemon", "u1", It.IsAny<JsonElement>()), Times.Once);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("\"[]\"")]
    [InlineData("\"\"")]
    public async Task EverySpellingOfNoAreasIsTheSameAlarm(string storedSpelling)
    {
        var body = "{\"pokemonId\":4,\"minIv\":50,\"distance\":2000," + NoOverride + "}";
        var twin = StoredTwinOf("pokemon", body, uid: 50, distance: 1000);
        twin["override_areas"] = JsonNode.Parse(storedSpelling);
        this.Store("pokemon", twin);

        await Assert.ThrowsAsync<TrackingConflictException>(() => this.CreateAsync("pokemon", body));
    }

    [Fact]
    public async Task AnEditFromTheUiThatWouldTakeOverAnotherAlarmIsRefused()
    {
        // The update half of the same guard. Raid 60 is being edited to the settings raid 50 holds, but
        // for auto-delete -- one updatable difference, so on v1 PoracleNG merges the edit into raid 50.
        var edited = "{\"pokemonId\":150,\"level\":9000,\"team\":4,\"exclusive\":0,\"form\":0,\"distance\":1000,\"clean\":0,\"gymId\":null," + NoOverride + "}";
        var other = StoredTwinOf("raid", edited, uid: 50, distance: 1000);
        other["clean"] = 1;
        var self = StoredTwinOf("raid", edited, uid: 60, distance: 500);
        this.Store("raid", other, self);

        var existing = JsonSerializer.Deserialize<Raid>(self.ToJsonString(), PoracleJsonHelper.SnakeCaseOptions)!;
        JsonSerializer.Deserialize<RaidUpdate>(edited, WebOptions)!.ApplyUpdate(existing);

        var sut = new RaidService(this._proxy.Object, this._gate.Object, NullLogger<RaidService>.Instance,
            this._remapper.Object, CostumeCapabilityDoubles.Supported());

        await Assert.ThrowsAsync<TrackingConflictException>(() => sut.UpdateAsync("u1", existing));
    }

    [Fact]
    public async Task ResavingAnUnchangedAlarmIsANoOpRatherThanAConflict()
    {
        // The no-op check shares the comparison. On v1 PoracleNG answers {alreadyPresent:1} to a resave,
        // and the dialog's [] against the stored null made an untouched alarm read as a collision.
        var body = "{\"team\":4,\"slotChanges\":0,\"battleChanges\":0,\"distance\":1000,\"clean\":0,\"gymId\":null," + NoOverride + "}";
        var self = StoredTwinOf("gym", body, uid: 60, distance: 1000);
        this.Store("gym", self);
        this._proxy
            .Setup(p => p.CreateAsync("gym", "u1", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([], 1, 0, 0));

        var existing = JsonSerializer.Deserialize<Gym>(self.ToJsonString(), PoracleJsonHelper.SnakeCaseOptions)!;
        JsonSerializer.Deserialize<GymUpdate>(body, WebOptions)!.ApplyUpdate(existing);

        var sut = new GymService(this._proxy.Object, this._gate.Object, NullLogger<GymService>.Instance, this._remapper.Object);

        Assert.Equal(60, (await sut.UpdateAsync("u1", existing)).Uid);
    }

    /// <summary>
    /// The stored row PoracleNG would hand back for the alarm the SPA body describes: snake_case, the
    /// template filled with its default, gym_id as "", and no override stored as NULL and "".
    /// </summary>
    private static JsonObject StoredTwinOf(string type, string spaBody, int uid, int distance)
    {
        var row = JsonSerializer.SerializeToNode(ToModel(type, spaBody), PoracleJsonHelper.SnakeCaseOptions)!.AsObject();
        row["uid"] = uid;
        row["id"] = "u1";
        row["profile_no"] = 1;
        row["distance"] = distance;
        row["template"] = "1";
        row["override_areas"] = null;
        row["override_location_label"] = "";
        if (row.ContainsKey("gym_id"))
        {
            row["gym_id"] = "";
        }

        if (row["change_types"] is JsonArray changeTypes)
        {
            // Stored as its JSON text.
            row["change_types"] = changeTypes.ToJsonString();
        }

        return row;
    }

    private void Store(string type, params JsonObject[] rows) =>
        this._proxy.Setup(p => p.GetByUserAsync(type, "u1"))
            .ReturnsAsync(() => JsonDocument.Parse(new JsonArray([.. rows.Select(r => r.DeepClone())]).ToJsonString()).RootElement.Clone());

    private static object ToModel(string type, string spaBody) => type switch
    {
        "pokemon" => JsonSerializer.Deserialize<MonsterCreate>(spaBody, WebOptions)!.ToMonster(),
        "raid" => JsonSerializer.Deserialize<RaidCreate>(spaBody, WebOptions)!.ToRaid(),
        "egg" => JsonSerializer.Deserialize<EggCreate>(spaBody, WebOptions)!.ToEgg(),
        "quest" => JsonSerializer.Deserialize<QuestCreate>(spaBody, WebOptions)!.ToQuest(),
        "nest" => JsonSerializer.Deserialize<NestCreate>(spaBody, WebOptions)!.ToNest(),
        "gym" => JsonSerializer.Deserialize<GymCreate>(spaBody, WebOptions)!.ToGym(),
        "fort" => JsonSerializer.Deserialize<FortChangeCreate>(spaBody, WebOptions)!.ToFortChange(),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "No fixture for this tracking type."),
    };

    private async Task CreateAsync(string type, string spaBody)
    {
        var model = ToModel(type, spaBody);
        switch (model)
        {
            case Monster m:
                await new MonsterService(this._proxy.Object, this._gate.Object, this._remapper.Object,
                    CostumeCapabilityDoubles.Supported()).CreateAsync("u1", m);
                break;
            case Raid r:
                await new RaidService(this._proxy.Object, this._gate.Object, NullLogger<RaidService>.Instance,
                    this._remapper.Object, CostumeCapabilityDoubles.Supported()).CreateAsync("u1", r);
                break;
            case Egg e:
                await new EggService(this._proxy.Object, this._gate.Object, NullLogger<EggService>.Instance,
                    this._remapper.Object).CreateAsync("u1", e);
                break;
            case Quest q:
                await new QuestService(this._proxy.Object, this._gate.Object, PokecoinCapabilityStub.Supported,
                    NullLogger<QuestService>.Instance, this._remapper.Object).CreateAsync("u1", q);
                break;
            case Nest n:
                await new NestService(this._proxy.Object, this._gate.Object, NullLogger<NestService>.Instance,
                    this._remapper.Object).CreateAsync("u1", n);
                break;
            case Gym g:
                await new GymService(this._proxy.Object, this._gate.Object, NullLogger<GymService>.Instance,
                    this._remapper.Object).CreateAsync("u1", g);
                break;
            case FortChange f:
                await new FortChangeService(this._proxy.Object, this._gate.Object,
                    NullLogger<FortChangeService>.Instance, this._remapper.Object).CreateAsync("u1", f);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(type), type, "No service for this tracking type.");
        }
    }
}
