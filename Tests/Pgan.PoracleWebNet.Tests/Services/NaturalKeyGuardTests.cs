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
/// Lure, invasion and pokestop-event guards follow the key PoracleNG's database actually carries.
/// </summary>
/// <remarks>
/// <para>
/// Up to schema 5 (5.1.0) <c>lures</c> and <c>invasion</c> carry a UNIQUE key on the natural key, so a
/// second rule for the same lure type answers <c>500 database error</c> however else it differs. Migration
/// 8 (5.2.x) drops both keys, and from then on PoracleNG stores those rules side by side. Verified by
/// posting straight to PoracleNG on 5.1.0, 5.2.1 and 5.3.0: an area-scoped twin, different areas, a place
/// label and two updatable differences are 500 on 5.1.0 and <c>insert:1</c> on the other two, and the same
/// four through <c>/api/v2/.../tracking/incident</c> create a second rule on 5.2.1 and 5.3.0.
/// </para>
/// <para>
/// So on a server without the keys these types use the guard the other seven use, which refuses only the
/// Add PoracleNG would resolve into another alarm. On a server with the keys the natural-key refusal stays,
/// because there every one of those Adds is a 500.
/// </para>
/// </remarks>
public class NaturalKeyGuardTests
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);
    private const string NoOverride = "\"overrideAreas\":[],\"overrideLocationLabel\":\"\"";

    private readonly Mock<IPoracleTrackingProxy> _proxy = new();
    private readonly Mock<IFeatureGate> _gate = new();
    private readonly Mock<ITrackedUidRemapper> _remapper = new();

    public NaturalKeyGuardTests()
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

    // ── 5.2.1 and later: no unique key ────────────────────────────────────────

    [Theory]
    [MemberData(nameof(SpaBodies))]
    public async Task WithoutTheKeyAnAddBesideAnAreaScopedTwinGoesThrough(string type, string body)
    {
        var twin = StoredTwinOf(type, body, uid: 50, distance: 0);
        twin["override_areas"] = new JsonArray("aberdeen");
        this.Store(type, twin);

        await this.CreateAsync(type, body, NaturalKeyStub.Dropped);

        this._proxy.Verify(p => p.CreateAsync(type, "u1", It.IsAny<JsonElement>()), Times.Once);
    }

    [Theory]
    [MemberData(nameof(SpaBodies))]
    public async Task WithoutTheKeyAnAddBesideADifferentAreaListGoesThrough(string type, string body)
    {
        var scoped = body.Replace(NoOverride, "\"overrideAreas\":[\"academia\"],\"overrideLocationLabel\":\"\"", StringComparison.Ordinal)
            .Replace("\"distance\":1000", "\"distance\":0", StringComparison.Ordinal);
        var twin = StoredTwinOf(type, scoped, uid: 50, distance: 0);
        twin["override_areas"] = new JsonArray("aberdeen");
        this.Store(type, twin);

        await this.CreateAsync(type, scoped, NaturalKeyStub.Dropped);

        this._proxy.Verify(p => p.CreateAsync(type, "u1", It.IsAny<JsonElement>()), Times.Once);
    }

    [Theory]
    [MemberData(nameof(SpaBodies))]
    public async Task WithoutTheKeyAnAddMeasuredFromAPlaceGoesThrough(string type, string body)
    {
        var placed = body.Replace(NoOverride, "\"overrideAreas\":[],\"overrideLocationLabel\":\"home\"", StringComparison.Ordinal);
        this.Store(type, StoredTwinOf(type, body, uid: 50, distance: 1000));

        await this.CreateAsync(type, placed, NaturalKeyStub.Dropped);

        this._proxy.Verify(p => p.CreateAsync(type, "u1", It.IsAny<JsonElement>()), Times.Once);
    }

    [Theory]
    [MemberData(nameof(SpaBodies))]
    public async Task WithoutTheKeyAnAddDifferingByTwoUpdatableFieldsGoesThrough(string type, string body)
    {
        var twin = StoredTwinOf(type, body, uid: 50, distance: 3000);
        twin["template"] = "2";
        this.Store(type, twin);

        await this.CreateAsync(type, body, NaturalKeyStub.Dropped);

        this._proxy.Verify(p => p.CreateAsync(type, "u1", It.IsAny<JsonElement>()), Times.Once);
    }

    [Theory]
    [MemberData(nameof(SpaBodies))]
    public async Task WithoutTheKeyAnAddThatWouldTakeOverAnExistingRuleIsStillRefused(string type, string body)
    {
        // One updatable difference: PoracleNG answers {"updates":1} and the stored rule is overwritten.
        this.Store(type, StoredTwinOf(type, body, uid: 50, distance: 1500));

        await Assert.ThrowsAsync<TrackingConflictException>(() => this.CreateAsync(type, body, NaturalKeyStub.Dropped));

        this._proxy.Verify(p => p.CreateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<JsonElement>()), Times.Never);
    }

    [Theory]
    [MemberData(nameof(SpaBodies))]
    public async Task WithoutTheKeyAnExactDuplicateWritesNothing(string type, string body)
    {
        // PoracleNG answers {"alreadyPresent":1} and names no uid; the controller then answers 200, not
        // 201, exactly as for the other seven types (#459).
        this.Store(type, StoredTwinOf(type, body, uid: 50, distance: 1000));
        this._proxy
            .Setup(p => p.CreateAsync(type, "u1", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([], 1, 0, 0));

        Assert.Equal(0, await this.CreateAsync(type, body, NaturalKeyStub.Dropped));
    }

    [Fact]
    public async Task WithoutTheKeyAGruntTypeInAnotherCaseIsASeparateRule()
    {
        // 5.2.1 stores "Water" beside "water" (insert:1); only the old case-insensitive key refused it.
        var body = "{\"gruntType\":\"Water\",\"gender\":0,\"distance\":1000,\"clean\":0," + NoOverride + "}";
        this.Store("invasion", StoredTwinOf("invasion", body.Replace("Water", "water", StringComparison.Ordinal), uid: 50, distance: 1500));

        await this.CreateAsync("invasion", body, NaturalKeyStub.Dropped);

        this._proxy.Verify(p => p.CreateAsync("invasion", "u1", It.IsAny<JsonElement>()), Times.Once);
    }

    [Fact]
    public async Task WithoutTheKeyAnEditBesideAnAreaScopedTwinIsNotRefused()
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

        var updated = await this.LureService(NaturalKeyStub.Dropped).UpdateAsync("u1", existing);

        Assert.Equal(61, updated.Uid);
    }

    [Fact]
    public async Task WithoutTheKeyAnEditUsesTheSharedUpsertRatherThanDeleteAndRecreate()
    {
        // With no unique key to free there is nothing for the delete-first replace to do, and it leaves a
        // window in which the alarm exists nowhere. The v1 path is the one the other seven take: a create
        // carrying the uid, then the reconciler drops the superseded row.
        var body = "{\"gruntType\":\"water\",\"gender\":0,\"distance\":2000,\"clean\":0," + NoOverride + "}";
        var self = StoredTwinOf("invasion", body, uid: 60, distance: 1000);
        this.Store("invasion", self);
        var order = new List<string>();
        this._proxy
            .Setup(p => p.CreateAsync("invasion", "u1", It.IsAny<JsonElement>()))
            .Callback(() => order.Add("create"))
            .ReturnsAsync(new TrackingCreateResult([61], 0, 1, 0));
        this._proxy
            .Setup(p => p.DeleteByUidAsync("invasion", "u1", It.IsAny<int>()))
            .Callback(() => order.Add("delete"))
            .Returns(Task.CompletedTask);

        var existing = JsonSerializer.Deserialize<Invasion>(self.ToJsonString(), PoracleJsonHelper.SnakeCaseOptions)!;
        existing.Distance = 2000;

        var updated = await this.InvasionService(NaturalKeyStub.Dropped).UpdateAsync("u1", existing);

        Assert.Equal(61, updated.Uid);
        Assert.Equal(["create", "delete"], order);
    }

    // ── 5.1.0: the key is there ───────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(SpaBodies))]
    public async Task WithTheKeyAnAddBesideAnAreaScopedTwinIsStillRefused(string type, string body)
    {
        // The legitimate-case half of this change: on 5.1.0 this Add is a 500 upstream.
        var twin = StoredTwinOf(type, body, uid: 50, distance: 0);
        twin["override_areas"] = new JsonArray("aberdeen");
        this.Store(type, twin);

        await Assert.ThrowsAsync<TrackingConflictException>(() => this.CreateAsync(type, body, NaturalKeyStub.Enforced));

        this._proxy.Verify(p => p.CreateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<JsonElement>()), Times.Never);
    }

    [Fact]
    public async Task WithTheKeyAGruntTypeInAnotherCaseIsStillRefused()
    {
        // invasion_tracking is case-insensitive at the database: "Water" beside "water" is a 500 on 5.1.0.
        var body = "{\"gruntType\":\"Water\",\"gender\":0,\"distance\":1000,\"clean\":0," + NoOverride + "}";
        this.Store("invasion", StoredTwinOf("invasion", body.Replace("Water", "water", StringComparison.Ordinal), uid: 50, distance: 3000));

        await Assert.ThrowsAsync<TrackingConflictException>(() => this.CreateAsync("invasion", body, NaturalKeyStub.Enforced));
    }

    [Fact]
    public async Task WithTheKeyAnEditStillFreesTheKeyBeforeRecreating()
    {
        // 5.1.0 and a 5.2.x that declined v2: the v1 create would collide with the row being edited.
        var body = "{\"lureId\":501,\"distance\":2000,\"clean\":0," + NoOverride + "}";
        var self = StoredTwinOf("lure", body, uid: 60, distance: 1000);
        this.Store("lure", self);
        var order = new List<string>();
        this._proxy
            .Setup(p => p.CreateAsync("lure", "u1", It.IsAny<JsonElement>()))
            .Callback(() => order.Add("create"))
            .ReturnsAsync(new TrackingCreateResult([61], 0, 0, 1));
        this._proxy
            .Setup(p => p.DeleteByUidAsync("lure", "u1", It.IsAny<int>()))
            .Callback(() => order.Add("delete"))
            .Returns(Task.CompletedTask);

        var existing = JsonSerializer.Deserialize<Lure>(self.ToJsonString(), PoracleJsonHelper.SnakeCaseOptions)!;
        existing.Distance = 2000;

        await this.LureService(NaturalKeyStub.Enforced).UpdateAsync("u1", existing);

        Assert.Equal(["delete", "create"], order);
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

    private PokestopEventService Events(INaturalKeyCapabilityService keys, params PokestopEvent[] stored)
    {
        this._incidents.Setup(p => p.GetByUserAsync("u1")).ReturnsAsync(stored);
        this._incidents
            .Setup(p => p.CreateAsync("u1", It.IsAny<IEnumerable<PokestopEvent>>()))
            .ReturnsAsync(new PokestopEventWriteResult([Event(900)], [], []));
        this._incidents
            .Setup(p => p.ReplaceAsync("u1", It.IsAny<int>(), It.IsAny<PokestopEvent>()))
            .ReturnsAsync(new PokestopEventWriteResult([Event(901)], [], []));
        return new PokestopEventService(this._incidents.Object, this._gate.Object, keys);
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

        var created = await this.Events(NaturalKeyStub.Dropped, stored).CreateAsync("u1", submitted);

        Assert.Equal(900, created.Uid);
    }

    [Theory]
    [MemberData(nameof(CoexistingEventTwins))]
    public async Task AnEventRuleWithSuchATwinStaysEditable(string shape)
    {
        var (stored, submitted) = EventPair(shape);
        submitted.Uid = 60;

        var updated = await this.Events(NaturalKeyStub.Dropped, stored, Event(60, distance: 700, template: "5"))
            .UpdateAsync("u1", submitted);

        Assert.Equal(901, updated.Uid);
    }

    [Theory]
    [InlineData(1500, null)] // one updatable difference: PoracleNG rewrites the stored rule
    [InlineData(1000, null)] // exact duplicate
    [InlineData(1000, "2")]  // template only
    public async Task AnEventAddThatWouldMergeIntoAnExistingRuleIsStillRefused(int distance, string? template)
    {
        var service = this.Events(NaturalKeyStub.Dropped, Event(50));

        await Assert.ThrowsAsync<TrackingConflictException>(
            () => service.CreateAsync("u1", Event(0, distance: distance, template: template)));
    }

    [Fact]
    public async Task AnEventAddBesideAnAreaTwinIsRefusedWhereTheKeyStillExists()
    {
        var service = this.Events(NaturalKeyStub.Enforced, Event(50, distance: 0, areas: ["aberdeen"]));

        await Assert.ThrowsAsync<TrackingConflictException>(() => service.CreateAsync("u1", Event(0)));
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private LureService LureService(INaturalKeyCapabilityService keys) =>
        new(this._proxy.Object, this._gate.Object, NullLogger<LureService>.Instance, this._remapper.Object, keys);

    private InvasionService InvasionService(INaturalKeyCapabilityService keys) =>
        new(this._proxy.Object, this._gate.Object, NullLogger<InvasionService>.Instance, this._remapper.Object, keys);

    private async Task<int> CreateAsync(string type, string spaBody, INaturalKeyCapabilityService keys) => type switch
    {
        "lure" => (await this.LureService(keys).CreateAsync("u1", JsonSerializer.Deserialize<LureCreate>(spaBody, WebOptions)!.ToLure())).Uid,
        "invasion" => (await this.InvasionService(keys).CreateAsync("u1", JsonSerializer.Deserialize<InvasionCreate>(spaBody, WebOptions)!.ToInvasion())).Uid,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "No service for this tracking type."),
    };

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
