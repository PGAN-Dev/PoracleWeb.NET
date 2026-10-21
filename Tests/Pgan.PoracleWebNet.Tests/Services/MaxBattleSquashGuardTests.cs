using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// From PoracleNG 5.4.0 (jfberry/PoracleNG#328) max battles merge like raids: <c>clean</c>,
/// <c>distance</c> and <c>template</c> are updatable, so a write one of those apart from an existing max
/// battle updates that alarm instead of adding a second. Up to 5.3.0 every write inserts. Each refusal
/// here has a passing twin with the capability off, because the same request is legitimate on an older
/// server. See #1003.
/// </summary>
public class MaxBattleSquashGuardTests
{
    private readonly Mock<IPoracleTrackingProxy> _proxy = new();
    private readonly Mock<IFeatureGate> _featureGate = new();
    private readonly Mock<ITrackedUidRemapper> _remapper = new();

    public MaxBattleSquashGuardTests()
    {
        this._featureGate.Setup(g => g.EnsureEnabledAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        this._proxy.Setup(p => p.CreateAsync("maxbattle", "u1", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([99], 0, 0, 1));
    }

    private MaxBattleService Service(bool squashes) => new(
        this._proxy.Object,
        this._featureGate.Object,
        NullLogger<MaxBattleService>.Instance,
        this._remapper.Object,
        PoracleTrackingProxyTests.V2Schema(new PoracleV2Capabilities { Read = true, MaxBattleSquashes = squashes }));

    /// <summary>A stored max battle in the shape PoracleNG's v1 read returns it.</summary>
    private static string Row(int uid, int pokemonId = 150, int distance = 500, int clean = 0, string template = "1") =>
        $$"""
        {"uid":{{uid}},"id":"u1","profile_no":1,"pokemon_id":{{pokemonId}},"level":9000,"form":0,"move":9000,
         "gmax":0,"evolution":9000,"station_id":"","distance":{{distance}},"clean":{{clean}},"template":"{{template}}",
         "ping":"","override_areas":null,"override_location_label":""}
        """;

    private void Stored(params string[] rows) =>
        this._proxy.Setup(p => p.GetByUserAsync("maxbattle", "u1"))
            .ReturnsAsync(JsonDocument.Parse($"[{string.Join(',', rows)}]").RootElement.Clone());

    /// <summary>The body the add dialog sends: no override spelled as <c>[]</c> and <c>""</c>.</summary>
    private static MaxBattle FromDialog(int distance, int pokemonId = 150, int clean = 0) => new()
    {
        PokemonId = pokemonId,
        Level = 3,
        Distance = distance,
        Clean = clean,
        Template = "1",
        OverrideAreas = [],
        OverrideLocationLabel = "",
    };

    [Fact]
    public async Task AnAddOneRadiusApartIsRefusedWhereTheServerMerges()
    {
        this.Stored(Row(5, distance: 500));

        await Assert.ThrowsAsync<TrackingConflictException>(
            () => this.Service(squashes: true).CreateAsync("u1", FromDialog(900)));

        this._proxy.Verify(p => p.CreateAsync("maxbattle", "u1", It.IsAny<JsonElement>()), Times.Never);
    }

    [Fact]
    public async Task TheSameAddIsAllowedWhereTheServerInserts()
    {
        this.Stored(Row(5, distance: 500));

        var created = await this.Service(squashes: false).CreateAsync("u1", FromDialog(900));

        Assert.Equal(99, created.Uid);
    }

    [Fact]
    public async Task AnAddTwoUpdatableFieldsApartIsAllowedWhereTheServerMerges()
    {
        // Radius and auto-delete both differ: PoracleNG inserts a separate rule.
        this.Stored(Row(5, distance: 500, clean: 0));

        var created = await this.Service(squashes: true).CreateAsync("u1", FromDialog(900, clean: 1));

        Assert.Equal(99, created.Uid);
    }

    [Fact]
    public async Task AnAddForAnotherBossIsAllowedWhereTheServerMerges()
    {
        this.Stored(Row(5, pokemonId: 150, distance: 500));

        var created = await this.Service(squashes: true).CreateAsync("u1", FromDialog(900, pokemonId: 151));

        Assert.Equal(99, created.Uid);
    }

    [Fact]
    public async Task AnEditOfTheRowItselfIsAllowedWhereTheServerMerges()
    {
        this.Stored(Row(5, distance: 500));
        this._proxy.Setup(p => p.DeleteByUidAsync("maxbattle", "u1", 5)).Returns(Task.CompletedTask);

        var model = FromDialog(900);
        model.Uid = 5;
        var updated = await this.Service(squashes: true).UpdateAsync("u1", model);

        Assert.Equal(99, updated.Uid);
    }

    /// <summary>
    /// The v1 edit deletes first. Uid 5 classifies against itself first, which the upsert guards accept,
    /// but once it is deleted PoracleNG resolves the create against uid 6 and rewrites it.
    /// </summary>
    [Fact]
    public async Task AnEditOneRadiusFromASiblingIsRefusedEvenWhenItsOwnRowComesFirst()
    {
        this.Stored(Row(5, distance: 500), Row(6, distance: 1500));

        var model = FromDialog(900);
        model.Uid = 5;

        await Assert.ThrowsAsync<TrackingConflictException>(
            () => this.Service(squashes: true).UpdateAsync("u1", model));

        this._proxy.Verify(p => p.DeleteByUidAsync("maxbattle", "u1", It.IsAny<int>()), Times.Never);
        this._proxy.Verify(p => p.CreateAsync("maxbattle", "u1", It.IsAny<JsonElement>()), Times.Never);
    }

    [Fact]
    public async Task TheSameEditIsAllowedWhereTheServerInserts()
    {
        this.Stored(Row(5, distance: 500), Row(6, distance: 1500));
        this._proxy.Setup(p => p.DeleteByUidAsync("maxbattle", "u1", 5)).Returns(Task.CompletedTask);

        var model = FromDialog(900);
        model.Uid = 5;
        var updated = await this.Service(squashes: false).UpdateAsync("u1", model);

        Assert.Equal(99, updated.Uid);
    }

    /// <summary>Two rows that differed only by radius become one alarm at the new radius.</summary>
    [Fact]
    public async Task ADistanceRewriteThatCollapsesTwoAlarmsIsRefusedWhereTheServerMerges()
    {
        this.Stored(Row(5, distance: 500), Row(6, distance: 1500));

        await Assert.ThrowsAsync<TrackingConflictException>(
            () => this.Service(squashes: true).UpdateDistanceByUidsAsync([5, 6], "u1", 800));

        this._proxy.Verify(p => p.BulkDeleteByUidsAsync("maxbattle", "u1", It.IsAny<List<int>>()), Times.Never);
    }

    /// <summary>
    /// The selected row is deleted, so the re-created one at the new radius is resolved against the
    /// unselected one and rewrites it.
    /// </summary>
    [Fact]
    public async Task ADistanceRewriteThatWouldTakeOverAnUnselectedAlarmIsRefusedWhereTheServerMerges()
    {
        this.Stored(Row(5, distance: 500), Row(6, distance: 1500));

        await Assert.ThrowsAsync<TrackingConflictException>(
            () => this.Service(squashes: true).UpdateDistanceByUidsAsync([5], "u1", 800));

        this._proxy.Verify(p => p.BulkDeleteByUidsAsync("maxbattle", "u1", It.IsAny<List<int>>()), Times.Never);
    }

    [Fact]
    public async Task TheSameDistanceRewritesAreAllowedWhereTheServerInserts()
    {
        this.Stored(Row(5, distance: 500), Row(6, distance: 1500));

        Assert.Equal(1, (await this.Service(squashes: false).UpdateDistanceByUidsAsync([5], "u1", 800)).Updated);
        Assert.Equal(2, (await this.Service(squashes: false).UpdateDistanceByUserAsync("u1", 1, 800)).Updated);
    }

    [Fact]
    public async Task ADistanceRewriteOfUnrelatedAlarmsIsAllowedWhereTheServerMerges()
    {
        this.Stored(Row(5, pokemonId: 150, distance: 500), Row(6, pokemonId: 151, distance: 1500));

        Assert.Equal(2, (await this.Service(squashes: true).UpdateDistanceByUserAsync("u1", 1, 800)).Updated);
    }

    [Fact]
    public async Task ADistanceRewriteAwayFromAnUnselectedSiblingByTwoFieldsIsAllowedWhereTheServerMerges()
    {
        this.Stored(Row(5, distance: 500, clean: 0), Row(6, distance: 1500, clean: 1));

        Assert.Equal(1, (await this.Service(squashes: true).UpdateDistanceByUidsAsync([5], "u1", 800)).Updated);
    }
}
