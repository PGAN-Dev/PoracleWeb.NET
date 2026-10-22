using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// "Update Distance" over a selection that includes an alarm whose scope a radius cannot apply to.
/// </summary>
/// <remarks>
/// <para>
/// An alarm limited to areas cannot also carry a radius, and one measured from a saved place cannot
/// drop to zero -- PoracleNG refuses both, and so does <see cref="OverrideScope"/> before
/// it. The bulk paths rewrote every selected row, so one area-scoped alarm in the selection failed the
/// whole batch with 400 "An alarm limited to areas cannot also have a radius" and nothing changed.
/// Worse, max battles delete their rows before re-creating them, so the refusal landed after the delete.
/// </para>
/// <para>
/// Those rows are now skipped, the rest are written, and the response names what was skipped.
/// </para>
/// <para>
/// The write is one v2 <c>PATCH</c> per type naming the uids and the radius alone. PoracleNG decides for
/// itself which rows would collide with another rule at the new radius and refuses those; they are reported
/// as <c>SkippedConflict</c> rather than failing the rest.
/// </para>
/// </remarks>
public class BulkDistanceScopeTests
{
    private readonly Mock<IPoracleTrackingProxy> _inner = new();
    private readonly Mock<IPoracleIncidentProxy> _incident = new();
    private readonly Mock<IFeatureGate> _featureGate = new();
    private readonly Mock<ITrackedUidRemapper> _remapper = new();
    private readonly List<(string Type, List<int> Uids, TrackingPatchSet Set)> _patched = [];
    private readonly List<(List<int> Uids, TrackingPatchSet Set)> _incidentPatched = [];
    private readonly Dictionary<int, PatchRefusal> _refusals = [];
    private readonly HashSet<int> _unchanged = [];
    private readonly IPoracleTrackingProxy _proxy;

    /// <summary>One alarm of each scope, as PoracleNG 5.2.1 reads them back.</summary>
    private const string StoredRows = """
        [
          {"uid":1,"id":"u1","profile_no":1,"pokemon_id":201,"distance":500,"clean":0,"template":"1","level":5,
           "grunt_type":"water","gender":0,"lure_id":501,"override_location_label":"","override_areas":null},
          {"uid":2,"id":"u1","profile_no":1,"pokemon_id":202,"distance":0,"clean":0,"template":"1","level":4,
           "grunt_type":"fire","gender":0,"lure_id":502,"override_location_label":"","override_areas":["aberdeen"]},
          {"uid":3,"id":"u1","profile_no":1,"pokemon_id":203,"distance":700,"clean":0,"template":"1","level":3,
           "grunt_type":"grass","gender":0,"lure_id":503,"override_location_label":"work","override_areas":null}
        ]
        """;

    public BulkDistanceScopeTests()
    {
        this._featureGate.Setup(g => g.EnsureEnabledAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        this._featureGate.Setup(g => g.IsEnabledAsync(It.IsAny<string>())).ReturnsAsync(false);
        this._inner
            .Setup(p => p.GetByUserAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(() => JsonDocument.Parse(StoredRows).RootElement.Clone());
        this._inner
            .Setup(p => p.PatchAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<int>>(), It.IsAny<TrackingPatchSet>()))
            .ReturnsAsync((string type, string _, IReadOnlyList<int> uids, TrackingPatchSet set) =>
            {
                this._patched.Add((type, [.. uids], set));
                return this.Answer(uids);
            });
        this._incident
            .Setup(p => p.PatchAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<int>>(), It.IsAny<TrackingPatchSet>()))
            .ReturnsAsync((string _, IReadOnlyList<int> uids, TrackingPatchSet set) =>
            {
                this._incidentPatched.Add(([.. uids], set));
                return this.Answer(uids);
            });

        this._proxy = this._inner.Object;
    }

    public static TheoryData<string> AllTrackingTypes() =>
    [
        "pokemon", "raid", "egg", "quest", "invasion", "lure", "nest", "gym", "fort", "maxbattle",
    ];

    [Theory]
    [MemberData(nameof(AllTrackingTypes))]
    public async Task BulkDistanceAreaScopedNotSent(string type)
    {
        var result = await UpdateSelected(this.ServiceFor(type), [1, 2, 3], 1500);

        // 1 has no override and 3 is measured from a place: both take a radius. 2 is limited to areas.
        Assert.Equal([1, 3], this.UidsSent());
        Assert.Equal(2, result.Updated);
        Assert.Equal([2], result.SkippedAreaScoped);
        Assert.Empty(result.SkippedPlaceScoped);
        Assert.Empty(result.SkippedConflict);
    }

    [Theory]
    [MemberData(nameof(AllTrackingTypes))]
    public async Task BulkDistanceUsesOnePatchNoPost(string type)
    {
        await UpdateAll(this.ServiceFor(type), 1500);

        var (patchedType, _, set) = Assert.Single(this._patched);
        Assert.Equal(type, patchedType);
        Assert.Equal(new TrackingPatchSet(Distance: 1500), set);
        this._inner.Verify(
            p => p.CreateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<JsonElement>()), Times.Never);
        this._inner.Verify(
            p => p.UpdateByUidAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<JsonElement>()),
            Times.Never);
        this._inner.Verify(
            p => p.BulkDeleteByUidsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<int>>()),
            Times.Never);
    }

    [Theory]
    [MemberData(nameof(AllTrackingTypes))]
    public async Task BulkDistanceConflictRefusalReportedAsSkippedConflict(string type)
    {
        this._refusals[3] = new PatchRefusal(3, PatchRefusalReasons.Conflict, "would duplicate rule 1", 1);

        var result = await UpdateSelected(this.ServiceFor(type), [1, 2, 3], 1500);

        // The legitimate row beside the refused one is still written and counted.
        Assert.Equal([1, 3], this.UidsSent());
        Assert.Equal(1, result.Updated);
        Assert.Equal([3], result.SkippedConflict);
        Assert.Equal([2], result.SkippedAreaScoped);
    }

    [Theory]
    [MemberData(nameof(AllTrackingTypes))]
    public async Task BulkDistanceCountsUnchangedAsUpdated(string type)
    {
        this._unchanged.Add(1);

        var result = await UpdateSelected(this.ServiceFor(type), [1, 3], 1500);

        Assert.Equal(2, result.Updated);
    }

    [Fact]
    public async Task BulkDistanceInvalidRefusalTheScopeCannotExplainIsAConflict()
    {
        // At zero the area-scoped row is sent, because zero is the radius it holds. Its scope therefore cannot
        // be why PoracleNG found it invalid, and calling it "limited to areas" would tell the user something
        // false. Before PATCH was sent trusted, that is exactly how a rule limited to the user's own fence came
        // back. Every invalid refusal of a row that was sent is a conflict, with a warning in the log.
        this._refusals[1] = new PatchRefusal(1, PatchRefusalReasons.Invalid, "bad", null);
        this._refusals[2] = new PatchRefusal(2, PatchRefusalReasons.Invalid, "bad", null);

        var result = await UpdateSelected(this.ServiceFor("raid"), [1, 2, 3], 0);

        Assert.Equal(0, result.Updated);
        Assert.Empty(result.SkippedAreaScoped);
        Assert.Equal([3], result.SkippedPlaceScoped);
        Assert.Equal([1, 2], result.SkippedConflict);
    }

    [Fact]
    public async Task BulkDistanceNotFoundRefusalIsNeitherCountedNorReported()
    {
        this._refusals[1] = new PatchRefusal(1, PatchRefusalReasons.NotFound, "gone", null);

        var result = await UpdateSelected(this.ServiceFor("gym"), [1, 3], 1500);

        Assert.Equal(1, result.Updated);
        Assert.Empty(result.SkippedConflict);
        Assert.Empty(result.SkippedAreaScoped);
        Assert.Empty(result.SkippedPlaceScoped);
    }

    [Theory]
    [MemberData(nameof(AllTrackingTypes))]
    public async Task ARadiusOverEveryAlarmSkipsTheAreaScopedAlarmAndUpdatesTheRest(string type)
    {
        var result = await UpdateAll(this.ServiceFor(type), 1500);

        Assert.Equal([1, 3], this.UidsSent());
        Assert.Equal(2, result.Updated);
        Assert.Equal([2], result.SkippedAreaScoped);
    }

    [Theory]
    [MemberData(nameof(AllTrackingTypes))]
    public async Task ZeroSkipsThePlaceScopedAlarmAndStillReachesTheAreaScopedOne(string type)
    {
        // The legitimate case for area-scoped rules: zero is exactly the radius they hold, so they are
        // written, not skipped. A place needs a radius, so that one is what zero skips.
        var result = await UpdateSelected(this.ServiceFor(type), [1, 2, 3], 0);

        Assert.Equal([1, 2], this.UidsSent());
        Assert.Equal(2, result.Updated);
        Assert.Empty(result.SkippedAreaScoped);
        Assert.Equal([3], result.SkippedPlaceScoped);
    }

    [Theory]
    [MemberData(nameof(AllTrackingTypes))]
    public async Task ASelectionOfOnlyAreaScopedAlarmsWritesNothingAndSaysWhy(string type)
    {
        var result = await UpdateSelected(this.ServiceFor(type), [2], 1500);

        Assert.Empty(this._patched);
        Assert.Equal(0, result.Updated);
        Assert.Equal([2], result.SkippedAreaScoped);
    }

    [Fact]
    public async Task MaxBattleBulkDistanceNoDelete()
    {
        // Max battles used to delete then re-create, so a failure after the delete lost the alarms.
        var result = await UpdateSelected(this.ServiceFor("maxbattle"), [1, 2, 3], 1500);

        Assert.Equal(2, result.Updated);
        Assert.Equal([1, 3], this.UidsSent());
        this._inner.Verify(
            p => p.BulkDeleteByUidsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<int>>()),
            Times.Never);
        this._inner.Verify(
            p => p.DeleteByUidAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task InvasionBulkDistanceSplitsEventsToIncident()
    {
        // With no Pokestop Events page the invasion list holds the event rules, so its bulk distance reaches
        // them -- through incident, since v2's invasion route does not hold them.
        this._inner
            .Setup(p => p.GetByUserAsync("invasion", "u1"))
            .ReturnsAsync(JsonDocument.Parse("""
                [
                  {"uid":1,"id":"u1","grunt_type":"water","gender":0,"distance":500,"override_location_label":"","override_areas":null},
                  {"uid":7,"id":"u1","grunt_type":"kecleon","gender":0,"distance":500,"override_location_label":"","override_areas":null},
                  {"uid":3,"id":"u1","grunt_type":"fire","gender":0,"distance":500,"override_location_label":"","override_areas":null}
                ]
                """).RootElement.Clone());

        var result = await UpdateSelected(this.ServiceFor("invasion"), [1, 7, 3], 1500);

        var (type, uids, set) = Assert.Single(this._patched);
        Assert.Equal("invasion", type);
        Assert.Equal([1, 3], uids);
        Assert.Equal(new TrackingPatchSet(Distance: 1500), set);
        var (eventUids, eventSet) = Assert.Single(this._incidentPatched);
        Assert.Equal([7], eventUids);
        Assert.Equal(new TrackingPatchSet(Distance: 1500), eventSet);
        Assert.Equal(3, result.Updated);
    }

    [Fact]
    public async Task InvasionBulkDistanceLeavesEventsAloneWhereTheyHaveTheirOwnPage()
    {
        this._featureGate.Setup(g => g.IsEnabledAsync(DisableFeatureKeys.PokestopEvents)).ReturnsAsync(true);
        this._inner
            .Setup(p => p.GetByUserAsync("invasion", "u1"))
            .ReturnsAsync(JsonDocument.Parse("""
                [
                  {"uid":1,"id":"u1","grunt_type":"water","gender":0,"distance":500},
                  {"uid":7,"id":"u1","grunt_type":"kecleon","gender":0,"distance":500}
                ]
                """).RootElement.Clone());

        var result = await UpdateAll(this.ServiceFor("invasion"), 1500);

        Assert.Equal([1], Assert.Single(this._patched).Uids);
        Assert.Empty(this._incidentPatched);
        Assert.Equal(1, result.Updated);
    }

    [Fact]
    public async Task PokestopEventsSkipTheSameWay()
    {
        this._incident.Setup(p => p.GetByUserAsync("u1")).ReturnsAsync(
        [
            new PokestopEvent { Uid = 1, DisplayType = PokestopEventTypes.Showcase, Distance = 100 },
            new PokestopEvent { Uid = 2, DisplayType = PokestopEventTypes.Kecleon, OverrideAreas = ["aberdeen"] },
        ]);

        var result = await new PokestopEventService(this._incident.Object, this._featureGate.Object, NullLogger<PokestopEventService>.Instance)
            .UpdateDistanceByUidsAsync([1, 2], "u1", 750);

        var (uids, set) = Assert.Single(this._incidentPatched);
        Assert.Equal([1], uids);
        Assert.Equal(new TrackingPatchSet(Distance: 750), set);
        Assert.Equal(1, result.Updated);
        Assert.Equal([2], result.SkippedAreaScoped);
        this._incident.Verify(
            p => p.CreateAsync(It.IsAny<string>(), It.IsAny<IEnumerable<PokestopEvent>>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task PokestopEventConflictRefusalReportedAsSkippedConflict()
    {
        this._incident.Setup(p => p.GetByUserAsync("u1")).ReturnsAsync(
        [
            new PokestopEvent { Uid = 1, DisplayType = PokestopEventTypes.Showcase, Distance = 100 },
            new PokestopEvent { Uid = 2, DisplayType = PokestopEventTypes.Kecleon, Distance = 100 },
        ]);
        this._refusals[2] = new PatchRefusal(2, PatchRefusalReasons.Conflict, "duplicate", 1);

        var result = await new PokestopEventService(this._incident.Object, this._featureGate.Object, NullLogger<PokestopEventService>.Instance)
            .UpdateDistanceByUserAsync("u1", 0, 750);

        Assert.Equal(1, result.Updated);
        Assert.Equal([2], result.SkippedConflict);
    }

    private TrackingPatchResult Answer(IReadOnlyList<int> uids) => new(
        [.. uids.Where(u => !this._refusals.ContainsKey(u) && !this._unchanged.Contains(u))],
        [.. uids.Where(this._unchanged.Contains)],
        [.. uids.Where(this._refusals.ContainsKey).Select(u => this._refusals[u])]);

    private List<int> UidsSent() => [.. Assert.Single(this._patched).Uids.Order()];

    private static Task<DistanceUpdateResult> UpdateAll(object service, int distance) => service switch
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

    private static Task<DistanceUpdateResult> UpdateSelected(object service, List<int> uids, int distance) => service switch
    {
        IMonsterService s => s.UpdateDistanceByUidsAsync(uids, "u1", distance),
        IRaidService s => s.UpdateDistanceByUidsAsync(uids, "u1", distance),
        IEggService s => s.UpdateDistanceByUidsAsync(uids, "u1", distance),
        IQuestService s => s.UpdateDistanceByUidsAsync(uids, "u1", distance),
        IInvasionService s => s.UpdateDistanceByUidsAsync(uids, "u1", distance),
        ILureService s => s.UpdateDistanceByUidsAsync(uids, "u1", distance),
        INestService s => s.UpdateDistanceByUidsAsync(uids, "u1", distance),
        IGymService s => s.UpdateDistanceByUidsAsync(uids, "u1", distance),
        IFortChangeService s => s.UpdateDistanceByUidsAsync(uids, "u1", distance),
        IMaxBattleService s => s.UpdateDistanceByUidsAsync(uids, "u1", distance),
        _ => throw new ArgumentOutOfRangeException(nameof(service)),
    };

    private object ServiceFor(string trackingType) => trackingType switch
    {
        "pokemon" => new MonsterService(this._proxy, this._featureGate.Object, this._remapper.Object, NullLogger<MonsterService>.Instance),
        "raid" => new RaidService(
            this._proxy, this._featureGate.Object, NullLogger<RaidService>.Instance, this._remapper.Object),
        "egg" => new EggService(this._proxy, this._featureGate.Object, NullLogger<EggService>.Instance, this._remapper.Object),
        "quest" => new QuestService(
            this._proxy, this._featureGate.Object, NullLogger<QuestService>.Instance, this._remapper.Object),
        "invasion" => new InvasionService(this._proxy, this._featureGate.Object, NullLogger<InvasionService>.Instance, this._remapper.Object, this._incident.Object),
        "lure" => new LureService(this._proxy, this._featureGate.Object, NullLogger<LureService>.Instance, this._remapper.Object),
        "nest" => new NestService(this._proxy, this._featureGate.Object, NullLogger<NestService>.Instance, this._remapper.Object),
        "gym" => new GymService(this._proxy, this._featureGate.Object, NullLogger<GymService>.Instance, this._remapper.Object),
        "fort" => new FortChangeService(this._proxy, this._featureGate.Object, NullLogger<FortChangeService>.Instance, this._remapper.Object),
        "maxbattle" => new MaxBattleService(this._proxy, this._featureGate.Object, NullLogger<MaxBattleService>.Instance, this._remapper.Object),
        _ => throw new ArgumentOutOfRangeException(nameof(trackingType), trackingType, "unknown tracking type"),
    };
}
