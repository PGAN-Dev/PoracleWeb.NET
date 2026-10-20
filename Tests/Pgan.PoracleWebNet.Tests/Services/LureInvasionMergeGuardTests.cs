using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Mappings;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// Lure, invasion and pokestop-event writes refuse only what PoracleNG would merge into another rule.
/// </summary>
/// <remarks>
/// <para>
/// Up to schema 5 (PoracleNG 5.1.0) <c>lures</c> and <c>invasion</c> carried a UNIQUE key on the natural
/// key, so a second rule for the same lure type answered <c>500 database error</c> however else it
/// differed, and PoracleWeb.NET refused those Adds and edited by deleting the row first. Migration 8 (5.2.x)
/// drops both keys, and 5.2.1 is now the minimum, so that code is gone. Verified by posting straight to
/// PoracleNG on 5.2.1 and 5.3.0: an area-scoped twin, different areas, a place label and two updatable
/// differences are <c>insert:1</c>, and the same four through <c>/api/v2/.../tracking/incident</c> create a
/// second rule.
/// </para>
/// <para>
/// These types therefore use the guard every other type uses, which refuses only the Add or edit PoracleNG
/// would resolve into another alarm. The tests here are mostly the legitimate cases that must keep working,
/// since a refusal on the natural key alone is the easiest thing to reintroduce by accident.
/// </para>
/// </remarks>
public class LureInvasionMergeGuardTests
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);
    private const string NoOverride = "\"overrideAreas\":[],\"overrideLocationLabel\":\"\"";

    private readonly Mock<IPoracleTrackingProxy> _proxy = new();
    private readonly Mock<IFeatureGate> _gate = new();
    private readonly Mock<ITrackedUidRemapper> _remapper = new();

    public LureInvasionMergeGuardTests()
    {
        this._gate.Setup(g => g.EnsureEnabledAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        this._proxy
            .Setup(p => p.CreateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([77], 0, 0, 1));
    }

    /// <summary>What the lure and invasion add dialogs post, at 1 km with no override.</summary>
    public static TheoryData<string, string> SpaBodies() => new()
    {
        { "lure", "{\"lureId\":501,\"distance\":1000,\"clean\":0," + NoOverride + "}" },
        { "invasion", "{\"gruntType\":\"water\",\"gender\":0,\"distance\":1000,\"clean\":0," + NoOverride + "}" },
    };

    /// <summary>
    /// The grunt names production rules carry that v2 refuses, so every edit of them takes the v1 path:
    /// three Pokéstop events and the <c>metal</c> this application once wrote where the game data says
    /// <c>steel</c>. 32 of 201 production invasion rules carried one of these when #841 was written.
    /// </summary>
    public static TheoryData<string> GruntNamesV2Refuses() => ["kecleon", "gold-stop", "showcase", "metal"];

    // ── Adds ─────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(SpaBodies))]
    public async Task AnAddBesideAnAreaScopedTwinGoesThrough(string type, string body)
    {
        var twin = StoredTwinOf(type, body, uid: 50, distance: 0);
        twin["override_areas"] = new JsonArray("aberdeen");
        this.Store(type, twin);

        await this.CreateAsync(type, body);

        this._proxy.Verify(p => p.CreateAsync(type, "u1", It.IsAny<JsonElement>()), Times.Once);
    }

    [Theory]
    [MemberData(nameof(SpaBodies))]
    public async Task AnAddBesideADifferentAreaListGoesThrough(string type, string body)
    {
        var scoped = body.Replace(NoOverride, "\"overrideAreas\":[\"academia\"],\"overrideLocationLabel\":\"\"", StringComparison.Ordinal)
            .Replace("\"distance\":1000", "\"distance\":0", StringComparison.Ordinal);
        var twin = StoredTwinOf(type, scoped, uid: 50, distance: 0);
        twin["override_areas"] = new JsonArray("aberdeen");
        this.Store(type, twin);

        await this.CreateAsync(type, scoped);

        this._proxy.Verify(p => p.CreateAsync(type, "u1", It.IsAny<JsonElement>()), Times.Once);
    }

    [Theory]
    [MemberData(nameof(SpaBodies))]
    public async Task AnAddMeasuredFromAPlaceGoesThrough(string type, string body)
    {
        var placed = body.Replace(NoOverride, "\"overrideAreas\":[],\"overrideLocationLabel\":\"home\"", StringComparison.Ordinal);
        this.Store(type, StoredTwinOf(type, body, uid: 50, distance: 1000));

        await this.CreateAsync(type, placed);

        this._proxy.Verify(p => p.CreateAsync(type, "u1", It.IsAny<JsonElement>()), Times.Once);
    }

    [Theory]
    [MemberData(nameof(SpaBodies))]
    public async Task AnAddDifferingByTwoUpdatableFieldsGoesThrough(string type, string body)
    {
        var twin = StoredTwinOf(type, body, uid: 50, distance: 3000);
        twin["template"] = "2";
        this.Store(type, twin);

        await this.CreateAsync(type, body);

        this._proxy.Verify(p => p.CreateAsync(type, "u1", It.IsAny<JsonElement>()), Times.Once);
    }

    [Theory]
    [MemberData(nameof(SpaBodies))]
    public async Task AnAddThatWouldTakeOverAnExistingRuleIsRefused(string type, string body)
    {
        // One updatable difference: PoracleNG answers {"updates":1} and the stored rule is overwritten.
        this.Store(type, StoredTwinOf(type, body, uid: 50, distance: 1500));

        await Assert.ThrowsAsync<TrackingConflictException>(() => this.CreateAsync(type, body));

        this._proxy.Verify(p => p.CreateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<JsonElement>()), Times.Never);
    }

    [Theory]
    [MemberData(nameof(SpaBodies))]
    public async Task AnExactDuplicateWritesNothing(string type, string body)
    {
        // PoracleNG answers {"alreadyPresent":1} and names no uid; the controller then answers 200, not
        // 201, exactly as for the other types (#459).
        this.Store(type, StoredTwinOf(type, body, uid: 50, distance: 1000));
        this._proxy
            .Setup(p => p.CreateAsync(type, "u1", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([], 1, 0, 0));

        Assert.Equal(0, await this.CreateAsync(type, body));
    }

    [Fact]
    public async Task AGruntTypeInAnotherCaseIsASeparateRule()
    {
        // 5.2.1 stores "Water" beside "water" (insert:1); only 5.1.0's case-insensitive key refused it.
        var body = "{\"gruntType\":\"Water\",\"gender\":0,\"distance\":1000,\"clean\":0," + NoOverride + "}";
        this.Store("invasion", StoredTwinOf("invasion", body.Replace("Water", "water", StringComparison.Ordinal), uid: 50, distance: 1500));

        await this.CreateAsync("invasion", body);

        this._proxy.Verify(p => p.CreateAsync("invasion", "u1", It.IsAny<JsonElement>()), Times.Once);
    }

    [Theory]
    [MemberData(nameof(GruntNamesV2Refuses))]
    public async Task AnInvasionAddForAGruntNameV2RefusesGoesThrough(string gruntType)
    {
        var body = "{\"gruntType\":\"" + gruntType + "\",\"gender\":0,\"distance\":1000,\"clean\":0," + NoOverride + "}";
        this.Store("invasion");

        Assert.Equal(77, await this.CreateAsync("invasion", body));
    }

    // ── Edits ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AnEditBesideAnAreaScopedTwinIsNotRefused()
    {
        // The #553 shape: once the Add above is allowed, both rules must stay editable. Lure 60 has an
        // area-scoped twin 50 on the same lure type; moving 60 to 2 km is an ordinary edit.
        var body = "{\"lureId\":501,\"distance\":2000,\"clean\":0," + NoOverride + "}";
        var twin = StoredTwinOf("lure", body, uid: 50, distance: 0);
        twin["override_areas"] = new JsonArray("aberdeen");
        var self = StoredTwinOf("lure", body, uid: 60, distance: 1000);
        this.Store("lure", twin, self);
        this._proxy
            .Setup(p => p.CreateAsync("lure", "u1", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([61], 0, 1, 0));

        var existing = JsonSerializer.Deserialize<Lure>(self.ToJsonString(), PoracleJsonHelper.SnakeCaseOptions)!;
        existing.Distance = 2000;

        var updated = await this.LureService().UpdateAsync("u1", existing);

        Assert.Equal(61, updated.Uid);
    }

    [Theory]
    [MemberData(nameof(SpaBodies))]
    public async Task AnEditBesideARuleDifferingOnlyInDistanceTemplateAndCleanIsNotRefused(string type, string body)
    {
        // Two rules for one lure type or grunt that differ only in the three updatable fields coexist on
        // 5.2.1. Editing one so that it still differs from the other in more than one of them must go
        // through: that is exactly the edit a natural-key refusal used to block.
        var other = StoredTwinOf(type, body, uid: 50, distance: 5000);
        other["template"] = "2";
        other["clean"] = 1;
        var self = StoredTwinOf(type, body, uid: 60, distance: 1000);
        this.Store(type, other, self);
        this._proxy
            .Setup(p => p.CreateAsync(type, "u1", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([61], 0, 1, 0));

        // 60 moves to 2 km and keeps template 1 and clean 0: still three updatable fields away from 50.
        var updated = await this.UpdateAsync(type, self, distance: 2000);

        Assert.Equal(61, updated);
        this._proxy.Verify(p => p.DeleteByUidAsync(type, "u1", 50), Times.Never);
    }

    [Theory]
    [MemberData(nameof(SpaBodies))]
    public async Task AnEditOneUpdatableFieldAwayFromAnotherRuleIsRefused(string type, string body)
    {
        // The refusal half, so the case above is not passing because nothing is ever refused: moving 60 to
        // 5 km lands it one field (template) away from 50, which PoracleNG would merge into.
        var other = StoredTwinOf(type, body, uid: 50, distance: 5000);
        other["template"] = "2";
        var self = StoredTwinOf(type, body, uid: 60, distance: 1000);
        this.Store(type, other, self);

        await Assert.ThrowsAsync<TrackingConflictException>(() => this.UpdateAsync(type, self, distance: 5000));

        this._proxy.Verify(p => p.CreateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<JsonElement>()), Times.Never);
    }

    [Theory]
    [MemberData(nameof(SpaBodies))]
    public async Task AnEditUsesTheSharedUpsertRatherThanDeleteAndRecreate(string type, string body)
    {
        // With no unique key to free there is nothing for a delete-first replace to do, and it left a
        // window in which the alarm existed nowhere. The v1 path is the one the other types take: a create
        // carrying the uid, then the reconciler drops the superseded row.
        var self = StoredTwinOf(type, body, uid: 60, distance: 1000);
        this.Store(type, self);
        var order = this.RecordWrites(type, newUid: 61);

        var updated = await this.UpdateAsync(type, self, distance: 2000);

        Assert.Equal(61, updated);
        Assert.Equal(["create", "delete"], order);
    }

    [Theory]
    [MemberData(nameof(GruntNamesV2Refuses))]
    public async Task AnInvasionEditForAGruntNameV2RefusesSucceedsThroughV1(string gruntType)
    {
        // The proxy answers null for these names (TryReplaceV2Async declines), so the edit must land on
        // the shared v1 upsert -- not fail, and not delete the rule first.
        var body = "{\"gruntType\":\"" + gruntType + "\",\"gender\":0,\"distance\":1000,\"clean\":0," + NoOverride + "}";
        var self = StoredTwinOf("invasion", body, uid: 60, distance: 1000);
        this.Store("invasion", self);
        var order = this.RecordWrites("invasion", newUid: 61);

        var updated = await this.UpdateAsync("invasion", self, distance: 2000);

        Assert.Equal(61, updated);
        Assert.Equal(["create", "delete"], order);
        this._proxy.Verify(p => p.TryReplaceV2Async("invasion", "u1", 60, It.IsAny<JsonElement>()), Times.Once);
    }

    // ── pokestop events (v2 incident, stored in the invasion table) ───────────

    private readonly Mock<IPoracleIncidentProxy> _incidents = new();

    private static PokestopEvent Event(int uid, int distance = 1000, string? template = null, List<string>? areas = null, string? label = null) => new()
    {
        Uid = uid,
        DisplayType = PokestopEventTypes.Kecleon,
        Distance = distance,
        Template = template,
        OverrideAreas = areas,
        OverrideLocationLabel = label,
    };

    private PokestopEventService Events(params PokestopEvent[] stored)
    {
        this._incidents.Setup(p => p.GetByUserAsync("u1")).ReturnsAsync(stored);
        this._incidents
            .Setup(p => p.CreateAsync("u1", It.IsAny<IEnumerable<PokestopEvent>>()))
            .ReturnsAsync(new PokestopEventWriteResult([Event(900)], [], []));
        this._incidents
            .Setup(p => p.ReplaceAsync("u1", It.IsAny<int>(), It.IsAny<PokestopEvent>()))
            .ReturnsAsync(new PokestopEventWriteResult([Event(901)], [], []));
        return new PokestopEventService(this._incidents.Object, this._gate.Object);
    }

    public static TheoryData<string> CoexistingEventTwins() => ["area twin", "different areas", "place label", "two updatable diffs"];

    private static (PokestopEvent Stored, PokestopEvent Submitted) EventPair(string shape) => shape switch
    {
        "area twin" => (Event(50, distance: 0, areas: ["aberdeen"]), Event(0)),
        "different areas" => (Event(50, distance: 0, areas: ["aberdeen"]), Event(0, distance: 0, areas: ["academia"])),
        "place label" => (Event(50), Event(0, label: "home")),
        "two updatable diffs" => (Event(50, distance: 3000, template: "2"), Event(0)),
        _ => throw new ArgumentOutOfRangeException(nameof(shape)),
    };

    [Theory]
    [MemberData(nameof(CoexistingEventTwins))]
    public async Task AnEventAddPoracleStoresAsASecondRuleGoesThrough(string shape)
    {
        var (stored, submitted) = EventPair(shape);

        var created = await this.Events(stored).CreateAsync("u1", submitted);

        Assert.Equal(900, created.Uid);
    }

    [Theory]
    [MemberData(nameof(CoexistingEventTwins))]
    public async Task AnEventRuleWithSuchATwinStaysEditable(string shape)
    {
        var (stored, submitted) = EventPair(shape);
        submitted.Uid = 60;

        var updated = await this.Events(stored, Event(60, distance: 700, template: "5"))
            .UpdateAsync("u1", submitted);

        Assert.Equal(901, updated.Uid);
    }

    [Theory]
    [InlineData(1500, null)] // one updatable difference: PoracleNG rewrites the stored rule
    [InlineData(1000, null)] // exact duplicate
    [InlineData(1000, "2")]  // template only
    public async Task AnEventAddThatWouldMergeIntoAnExistingRuleIsRefused(int distance, string? template)
    {
        var service = this.Events(Event(50));

        await Assert.ThrowsAsync<TrackingConflictException>(
            () => service.CreateAsync("u1", Event(0, distance: distance, template: template)));
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private LureService LureService() =>
        new(this._proxy.Object, this._gate.Object, NullLogger<LureService>.Instance, this._remapper.Object);

    private InvasionService InvasionService() =>
        new(this._proxy.Object, this._gate.Object, NullLogger<InvasionService>.Instance, this._remapper.Object);

    private async Task<int> CreateAsync(string type, string spaBody) => type switch
    {
        "lure" => (await this.LureService().CreateAsync("u1", JsonSerializer.Deserialize<LureCreate>(spaBody, WebOptions)!.ToLure())).Uid,
        "invasion" => (await this.InvasionService().CreateAsync("u1", JsonSerializer.Deserialize<InvasionCreate>(spaBody, WebOptions)!.ToInvasion())).Uid,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "No service for this tracking type."),
    };

    /// <summary>Edits the stored rule <paramref name="self"/> to a new radius, as the edit dialog would.</summary>
    private async Task<int> UpdateAsync(string type, JsonObject self, int distance)
    {
        switch (type)
        {
            case "lure":
                var lure = JsonSerializer.Deserialize<Lure>(self.ToJsonString(), PoracleJsonHelper.SnakeCaseOptions)!;
                lure.Distance = distance;
                return (await this.LureService().UpdateAsync("u1", lure)).Uid;
            case "invasion":
                var invasion = JsonSerializer.Deserialize<Invasion>(self.ToJsonString(), PoracleJsonHelper.SnakeCaseOptions)!;
                invasion.Distance = distance;
                return (await this.InvasionService().UpdateAsync("u1", invasion)).Uid;
            default:
                throw new ArgumentOutOfRangeException(nameof(type), type, "No service for this tracking type.");
        }
    }

    /// <summary>Records the order of the v1 create and delete, answering the create with a re-keyed uid.</summary>
    private List<string> RecordWrites(string type, int newUid)
    {
        var order = new List<string>();
        this._proxy
            .Setup(p => p.CreateAsync(type, "u1", It.IsAny<JsonElement>()))
            .Callback(() => order.Add("create"))
            .ReturnsAsync(new TrackingCreateResult([newUid], 0, 1, 0));
        this._proxy
            .Setup(p => p.DeleteByUidAsync(type, "u1", It.IsAny<int>()))
            .Callback(() => order.Add("delete"))
            .Returns(Task.CompletedTask);
        return order;
    }

    /// <summary>The row PoracleNG hands back for the rule the SPA body describes.</summary>
    private static JsonObject StoredTwinOf(string type, string spaBody, int uid, int distance)
    {
        object model = type switch
        {
            "lure" => JsonSerializer.Deserialize<LureCreate>(spaBody, WebOptions)!.ToLure(),
            "invasion" => JsonSerializer.Deserialize<InvasionCreate>(spaBody, WebOptions)!.ToInvasion(),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "No fixture for this tracking type."),
        };
        var row = JsonSerializer.SerializeToNode(model, PoracleJsonHelper.SnakeCaseOptions)!.AsObject();
        row["uid"] = uid;
        row["id"] = "u1";
        row["profile_no"] = 1;
        row["distance"] = distance;
        row["template"] = "1";
        row["override_areas"] = null;
        row["override_location_label"] = "";
        return row;
    }

    private void Store(string type, params JsonObject[] rows) =>
        this._proxy.Setup(p => p.GetByUserAsync(type, "u1"))
            .ReturnsAsync(() => JsonDocument.Parse(new JsonArray([.. rows.Select(r => r.DeepClone())]).ToJsonString()).RootElement.Clone());
}
