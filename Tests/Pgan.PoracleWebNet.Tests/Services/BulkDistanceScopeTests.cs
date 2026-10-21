using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;
using Pgan.PoracleWebNet.Tests.TestDoubles;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// "Update Distance" over a selection that includes an alarm whose scope a radius cannot apply to.
/// </summary>
/// <remarks>
/// <para>
/// An alarm limited to areas cannot also carry a radius, and one measured from a saved place cannot
/// drop to zero -- PoracleNG refuses both, and so does <see cref="UserOwnedOverrideAreaProxy"/> before
/// it. The bulk paths rewrote every selected row, so one area-scoped alarm in the selection failed the
/// whole batch with 400 "An alarm limited to areas cannot also have a radius" and nothing changed.
/// Worse, max battles delete their rows before re-creating them, so the refusal landed after the delete.
/// </para>
/// <para>
/// Those rows are now skipped, the rest are written, and the response names what was skipped.
/// </para>
/// </remarks>
public class BulkDistanceScopeTests
{
    private readonly Mock<IPoracleTrackingProxy> _inner = new();
    private readonly Mock<IFeatureGate> _featureGate = new();
    private readonly Mock<ITrackedUidRemapper> _remapper = new();
    private readonly List<JsonElement> _sent = [];
    private readonly List<List<int>> _deleted = [];
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
        this._remapper
            .Setup(r => r.RemapAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns(Task.CompletedTask);
        this._inner
            .Setup(p => p.GetByUserAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(() => JsonDocument.Parse(StoredRows).RootElement.Clone());
        this._inner
            .Setup(p => p.CreateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<JsonElement>()))
            .Callback<string, string, JsonElement>((_, _, body) => this._sent.Add(body.Clone()))
            .ReturnsAsync(new TrackingCreateResult([], 0, 1, 0));
        this._inner
            .Setup(p => p.BulkDeleteByUidsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<int>>()))
            .Callback<string, string, IEnumerable<int>>((_, _, uids) => this._deleted.Add([.. uids]))
            .Returns(Task.CompletedTask);

        // Through the real decorator, so a row it would refuse fails the test the way it failed live.
        var geofences = new Mock<IUserGeofenceRepository>();
        geofences.Setup(g => g.GetByHumanIdAsync(It.IsAny<string>())).ReturnsAsync([]);
        this._proxy = new UserOwnedOverrideAreaProxy(
            this._inner.Object, geofences.Object, Mock.Of<IUserAreaDualWriter>(),
            NullLogger<UserOwnedOverrideAreaProxy>.Instance);
    }

    public static TheoryData<string> AllTrackingTypes() =>
    [
        "pokemon", "raid", "egg", "quest", "invasion", "lure", "nest", "gym", "fort", "maxbattle",
    ];

    [Theory]
    [MemberData(nameof(AllTrackingTypes))]
    public async Task ARadiusOverASelectionSkipsTheAreaScopedAlarmAndUpdatesTheRest(string type)
    {
        var result = await UpdateSelected(this.ServiceFor(type), [1, 2, 3], 1500);

        // 1 has no override and 3 is measured from a place: both take a radius. 2 is limited to areas.
        Assert.Equal([1, 3], this.UidsSent());
        Assert.Equal(2, result.Updated);
        Assert.Equal([2], result.SkippedAreaScoped);
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

        Assert.Empty(this._sent);
        Assert.Equal(0, result.Updated);
        Assert.Equal([2], result.SkippedAreaScoped);
    }

    [Fact]
    public async Task MaxBattleDoesNotDeleteTheAlarmItIsGoingToSkip()
    {
        // Max battles delete then re-create. The refusal used to land after the delete.
        await UpdateSelected(this.ServiceFor("maxbattle"), [1, 2, 3], 1500);

        Assert.Equal([1, 3], Assert.Single(this._deleted));
    }

    [Fact]
    public async Task PokestopEventsSkipTheSameWay()
    {
        var proxy = new Mock<IPoracleIncidentProxy>();
        proxy.Setup(p => p.GetByUserAsync("u1")).ReturnsAsync(
        [
            new PokestopEvent { Uid = 1, DisplayType = PokestopEventTypes.Showcase, Distance = 100 },
            new PokestopEvent { Uid = 2, DisplayType = PokestopEventTypes.Kecleon, OverrideAreas = ["aberdeen"] },
        ]);
        List<PokestopEvent>? sent = null;
        proxy.Setup(p => p.CreateAsync("u1", It.IsAny<IEnumerable<PokestopEvent>>()))
            .Callback<string, IEnumerable<PokestopEvent>>((_, rules) => sent = [.. rules])
            .ReturnsAsync(new PokestopEventWriteResult([], [], []));

        var result = await new PokestopEventService(proxy.Object, this._featureGate.Object)
            .UpdateDistanceByUidsAsync([1, 2], "u1", 750);

        Assert.Equal(1, Assert.Single(sent!).Uid);
        Assert.Equal(1, result.Updated);
        Assert.Equal([2], result.SkippedAreaScoped);
    }

    private List<int> UidsSent() =>
        [.. Assert.Single(this._sent).EnumerateArray().Select(r => r.GetProperty("uid").GetInt32()).Order()];

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
        "pokemon" => new MonsterService(this._proxy, this._featureGate.Object, this._remapper.Object, CostumeCapabilityDoubles.Supported()),
        "raid" => new RaidService(
            this._proxy, this._featureGate.Object, NullLogger<RaidService>.Instance, this._remapper.Object, CostumeCapabilityDoubles.Supported()),
        "egg" => new EggService(this._proxy, this._featureGate.Object, NullLogger<EggService>.Instance, this._remapper.Object),
        "quest" => new QuestService(
            this._proxy, this._featureGate.Object, PokecoinCapabilityStub.Supported, NullLogger<QuestService>.Instance, this._remapper.Object),
        "invasion" => new InvasionService(this._proxy, this._featureGate.Object, NullLogger<InvasionService>.Instance, this._remapper.Object),
        "lure" => new LureService(this._proxy, this._featureGate.Object, NullLogger<LureService>.Instance, this._remapper.Object),
        "nest" => new NestService(this._proxy, this._featureGate.Object, NullLogger<NestService>.Instance, this._remapper.Object),
        "gym" => new GymService(this._proxy, this._featureGate.Object, NullLogger<GymService>.Instance, this._remapper.Object),
        "fort" => new FortChangeService(this._proxy, this._featureGate.Object, NullLogger<FortChangeService>.Instance, this._remapper.Object),
        "maxbattle" => new MaxBattleService(this._proxy, this._featureGate.Object, NullLogger<MaxBattleService>.Instance, this._remapper.Object),
        _ => throw new ArgumentOutOfRangeException(nameof(trackingType), trackingType, "unknown tracking type"),
    };
}
