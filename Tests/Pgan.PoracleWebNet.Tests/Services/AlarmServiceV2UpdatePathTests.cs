using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;
using Pgan.PoracleWebNet.Tests.TestDoubles;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// What each alarm service does once the proxy answers that <c>/api/v2</c> replaced the rule.
/// </summary>
/// <remarks>
/// <para>
/// The uid-addressed PUT is a genuine full replace, so every repair the v1 path wraps around its create
/// has to be skipped as a unit: the reconcile of a stray insert, lure's delete-to-free-the-natural-key,
/// max battle's delete-then-recreate. Running any of them against a write that already landed would be
/// worse than not moving the type at all.
/// </para>
/// <para>
/// The other half is the uid. v2's engine is delete-then-insert, so the replacement arrives under a new
/// uid, and quick-pick applied state has to follow the row or its "remove" button silently deletes
/// nothing. See #403.
/// </para>
/// </remarks>
public class AlarmServiceV2UpdatePathTests
{
    private readonly Mock<IPoracleTrackingProxy> _proxy = new();
    private readonly Mock<IFeatureGate> _featureGate = new();
    private readonly Mock<ITrackedUidRemapper> _remapper = new();

    public AlarmServiceV2UpdatePathTests()
    {
        this._featureGate.Setup(g => g.EnsureEnabledAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        this._remapper
            .Setup(r => r.RemapAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns(Task.CompletedTask);
        this._proxy
            .Setup(p => p.GetByUserAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(() => JsonDocument.Parse("[]").RootElement.Clone());
    }

    public static TheoryData<string> MovedTypes() =>
        ["raid", "egg", "quest", "nest", "gym", "maxbattle", "fort", "lure"];

    [Theory]
    [MemberData(nameof(MovedTypes))]
    public async Task AV2ReplaceSkipsTheWholeV1RepairPath(string type)
    {
        this.AcceptV2(type, newUid: 991);

        Assert.Equal(991, await this.UpdateAsync(type, uid: 990));

        // No create, so no reconcile. No delete, so no window where the alarm exists nowhere.
        this._proxy.Verify(
            p => p.CreateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<JsonElement>()), Times.Never);
        this._proxy.Verify(
            p => p.DeleteByUidAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Theory]
    [MemberData(nameof(MovedTypes))]
    public async Task TheRotatedUidIsCarriedIntoQuickPickAppliedState(string type)
    {
        this.AcceptV2(type, newUid: 991);

        await this.UpdateAsync(type, uid: 990);

        this._remapper.Verify(r => r.RemapAsync("u1", type, 990, 991), Times.Once);
    }

    [Theory]
    [MemberData(nameof(MovedTypes))]
    public async Task DecliningV2LeavesTheV1PathExactlyAsItWas(string type)
    {
        // The legitimate-case half. A 5.1.0 server, an operator pinned to v1, or a row v2 cannot carry
        // faithfully all answer null -- and the type has to write exactly what it wrote before.
        this._proxy
            .Setup(p => p.TryReplaceV2Async(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<JsonElement>()))
            .ReturnsAsync((TrackingUpdateResult?)null);
        this._proxy
            .Setup(p => p.CreateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([991], 0, 0, 1));

        await this.UpdateAsync(type, uid: 990);

        this._proxy.Verify(
            p => p.CreateAsync(type, "u1", It.IsAny<JsonElement>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task LureNoLongerDeletesItsRowToFreeTheNaturalKey()
    {
        // The single biggest reason to move this type. PoracleNG's v1 create has no upsert path for
        // lure_tracking(id, profile_no, lure_id), so editing a lure's distance had to delete the row,
        // re-create it, and restore the original if that failed. Verified live on 5.2.1: the v2 PUT
        // replaced lure 266 in place as 267, where the v1 create-carrying-a-uid inserted 264 alongside
        // 263 and left both.
        this.AcceptV2("lure", newUid: 267);

        var updated = await new LureService(
                this._proxy.Object, this._featureGate.Object, NullLogger<LureService>.Instance, this._remapper.Object)
            .UpdateAsync("u1", new Lure { Uid = 266, LureId = 501, Distance = 5000 });

        Assert.Equal(267, updated.Uid);
        this._proxy.Verify(p => p.DeleteByUidAsync("lure", "u1", 266), Times.Never);
    }

    [Fact]
    public async Task MaxBattleStopsBeingInsertOnly()
    {
        // Its v1 path deletes the row and creates a replacement, so a failed create leaves the user with
        // no alarm at all. The v2 PUT has no such window.
        this.AcceptV2("maxbattle", newUid: 92);

        var updated = await new MaxBattleService(
                this._proxy.Object, this._featureGate.Object, NullLogger<MaxBattleService>.Instance, this._remapper.Object)
            .UpdateAsync("u1", new MaxBattle { Uid = 91, PokemonId = 150 });

        Assert.Equal(92, updated.Uid);
        this._proxy.Verify(p => p.DeleteByUidAsync("maxbattle", "u1", 91), Times.Never);
    }

    private void AcceptV2(string type, int newUid) =>
        this._proxy
            .Setup(p => p.TryReplaceV2Async(type, It.IsAny<string>(), It.IsAny<int>(), It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingUpdateResult(newUid, true));

    private async Task<int> UpdateAsync(string type, int uid) => type switch
    {
        "raid" => (await new RaidService(
                this._proxy.Object, this._featureGate.Object, NullLogger<RaidService>.Instance,
                this._remapper.Object, CostumeCapabilityDoubles.Supported())
            .UpdateAsync("u1", new Raid { Uid = uid, PokemonId = 150 })).Uid,
        "egg" => (await new EggService(
                this._proxy.Object, this._featureGate.Object, NullLogger<EggService>.Instance, this._remapper.Object)
            .UpdateAsync("u1", new Egg { Uid = uid, Level = 5 })).Uid,
        "quest" => (await new QuestService(
                this._proxy.Object, this._featureGate.Object, PokecoinCapabilityStub.Supported,
                NullLogger<QuestService>.Instance, this._remapper.Object)
            .UpdateAsync("u1", new Quest { Uid = uid, RewardType = 7, Reward = 25 })).Uid,
        "nest" => (await new NestService(
                this._proxy.Object, this._featureGate.Object, NullLogger<NestService>.Instance, this._remapper.Object)
            .UpdateAsync("u1", new Nest { Uid = uid, PokemonId = 25 })).Uid,
        "gym" => (await new GymService(
                this._proxy.Object, this._featureGate.Object, NullLogger<GymService>.Instance, this._remapper.Object)
            .UpdateAsync("u1", new Gym { Uid = uid, Team = 4 })).Uid,
        "maxbattle" => (await new MaxBattleService(
                this._proxy.Object, this._featureGate.Object, NullLogger<MaxBattleService>.Instance, this._remapper.Object)
            .UpdateAsync("u1", new MaxBattle { Uid = uid, PokemonId = 150 })).Uid,
        "fort" => (await new FortChangeService(
                this._proxy.Object, this._featureGate.Object, NullLogger<FortChangeService>.Instance, this._remapper.Object)
            .UpdateAsync("u1", new FortChange { Uid = uid, FortType = "gym" })).Uid,
        "lure" => (await new LureService(
                this._proxy.Object, this._featureGate.Object, NullLogger<LureService>.Instance, this._remapper.Object)
            .UpdateAsync("u1", new Lure { Uid = uid, LureId = 501 })).Uid,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "No fixture for this tracking type."),
    };
}
