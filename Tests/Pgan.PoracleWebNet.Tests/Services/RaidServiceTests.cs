using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

public class RaidServiceTests
{
    private static readonly JsonSerializerOptions SnakeCaseOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly Mock<IPoracleTrackingProxy> _proxy = new();
    private readonly Mock<IFeatureGate> _featureGate = new();
    private readonly Mock<ITrackedUidRemapper> _uidRemapper = new();
    private readonly RaidService _sut;

    public RaidServiceTests()
    {
        this._featureGate.Setup(g => g.EnsureEnabledAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        this._sut = new RaidService(this._proxy.Object, this._featureGate.Object, NullLogger<RaidService>.Instance, this._uidRemapper.Object);
    }

    [Fact]
    public async Task GetByUserAsyncReturnsRaids()
    {
        var json = CreateJsonArray(new
        {
            uid = 1,
            pokemon_id = 150,
            level = 5,
            id = "user1"
        });
        this._proxy.Setup(p => p.GetByUserAsync("raid", "user1")).ReturnsAsync(json);

        var result = await this._sut.GetByUserAsync("user1", 1);

        Assert.Single(result);
        Assert.Equal(5, result.First().Level);
    }

    [Fact]
    public async Task GetByUidAsyncReturnsRaid()
    {
        var json = CreateJsonArray(new
        {
            uid = 1,
            pokemon_id = 150,
            id = "user1"
        });
        this._proxy.Setup(p => p.GetByUserAsync("raid", "user1")).ReturnsAsync(json);

        var result = await this._sut.GetByUidAsync("user1", 1);

        Assert.NotNull(result);
        Assert.Equal(150, result!.PokemonId);
    }

    [Fact]
    public async Task GetByUidAsyncReturnsNullWhenNotFound()
    {
        var json = CreateJsonArray();
        this._proxy.Setup(p => p.GetByUserAsync("raid", "user1")).ReturnsAsync(json);

        Assert.Null(await this._sut.GetByUidAsync("user1", 999));
    }

    [Fact]
    public async Task CreateAsyncSetsUserId()
    {
        var raid = new Raid { PokemonId = 150 };
        this._proxy.Setup(p => p.CreateAsync("raid", "user1", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([1], 0, 0, 1));
        this._proxy.Setup(p => p.GetByUserAsync("raid", "user1")).ReturnsAsync(CreateJsonArray(new
        {
            uid = 1,
            id = "user1",
            pokemon_id = 150,
            level = 9000,
        }));

        var result = await this._sut.CreateAsync("user1", raid);

        Assert.Equal("user1", result.Id);
    }

    /// <summary>
    /// PoracleNG rewrites level to 9000 when the alarm names a specific boss, so echoing the submitted
    /// model advertised a level the stored row does not have. See #523.
    /// </summary>
    [Fact]
    public async Task CreateAsyncReportsTheStoredRowRatherThanTheRequest()
    {
        this._proxy.Setup(p => p.CreateAsync("raid", "user1", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([7], 0, 0, 1));
        this._proxy.Setup(p => p.GetByUserAsync("raid", "user1")).ReturnsAsync(CreateJsonArray(new
        {
            uid = 7,
            id = "user1",
            pokemon_id = 150,
            level = 9000,
        }));

        var result = await this._sut.CreateAsync("user1", new Raid { PokemonId = 150, Level = 5 });

        Assert.Equal(9000, result.Level);
        Assert.Equal(7, result.Uid);
    }

    /// <summary>A read-back that fails must not fail the create, which already succeeded.</summary>
    [Fact]
    public async Task CreateAsyncStillAnswersWhenTheReadBackFindsNothing()
    {
        this._proxy.Setup(p => p.CreateAsync("raid", "user1", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([7], 0, 0, 1));
        this._proxy.Setup(p => p.GetByUserAsync("raid", "user1")).ReturnsAsync(CreateJsonArray());

        var result = await this._sut.CreateAsync("user1", new Raid { PokemonId = 150, Level = 5 });

        Assert.Equal(7, result.Uid);
    }

    [Fact]
    public async Task UpdateAsyncCallsProxy()
    {
        var raid = new Raid { Uid = 1 };
        this._proxy.Setup(p => p.CreateAsync("raid", "user1", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([], 0, 1, 0));

        await this._sut.UpdateAsync("user1", raid);

        this._proxy.Verify(p => p.CreateAsync("raid", "user1", It.IsAny<JsonElement>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsyncReturnsTrue()
    {
        this._proxy.Setup(p => p.DeleteByUidAsync("raid", "user1", 1)).Returns(Task.CompletedTask);
        Assert.True(await this._sut.DeleteAsync("user1", 1));
    }

    [Fact]
    public async Task DeleteAllByUserAsyncReturnsCount()
    {
        var json = CreateJsonArray(
            new
            {
                uid = 1,
                id = "u"
            },
            new
            {
                uid = 2,
                id = "u"
            },
            new
            {
                uid = 3,
                id = "u"
            });
        this._proxy.Setup(p => p.GetByUserAsync("raid", "u")).ReturnsAsync(json);
        this._proxy.Setup(p => p.BulkDeleteByUidsAsync("raid", "u", It.IsAny<IEnumerable<int>>()))
            .Returns(Task.CompletedTask);

        Assert.Equal(3, await this._sut.DeleteAllByUserAsync("u", 1));
    }

    /// <summary>
    /// Two rows that differ only by radius become the same alarm once both are set to the same radius,
    /// and PoracleNG resolves that inside the batch -- so the user ends up with fewer alarms than they
    /// selected, one still at its old radius, and a response claiming every one was updated. See #580.
    /// </summary>
    [Fact]
    public async Task UpdateDistanceRefusesWhenTwoSelectedAlarmsWouldBecomeIdentical()
    {
        this._proxy.Setup(p => p.GetByUserAsync("raid", "u1")).ReturnsAsync(CreateJsonArray(
            new { uid = 1, id = "u1", level = 5, distance = 500, template = "1" },
            new { uid = 2, id = "u1", level = 5, distance = 900, template = "1" }));

        await Assert.ThrowsAsync<TrackingConflictException>(
            () => this._sut.UpdateDistanceByUidsAsync([1, 2], "u1", 700));

        this._proxy.Verify(
            p => p.CreateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<JsonElement>()), Times.Never);
    }

    [Fact]
    public async Task UpdateDistanceByUserAsyncReturnsCount()
    {
        var json = CreateJsonArray(
            new
            {
                uid = 1,
                id = "u",
                distance = 0,
                template = "ZZrow1"
            },
            new
            {
                uid = 2,
                id = "u",
                distance = 0,
                template = "ZZsecond"
            });
        this._proxy.Setup(p => p.GetByUserAsync("raid", "u")).ReturnsAsync(json);
        this._proxy.Setup(p => p.CreateAsync("raid", "u", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([], 0, 2, 0));

        Assert.Equal(2, await this._sut.UpdateDistanceByUserAsync("u", 1, 100));
    }

    [Fact]
    public async Task CountByUserAsyncReturnsCount()
    {
        var json = CreateJsonArray(
            new
            {
                uid = 1,
                id = "u"
            },
            new
            {
                uid = 2,
                id = "u"
            },
            new
            {
                uid = 3,
                id = "u"
            },
            new
            {
                uid = 4,
                id = "u"
            },
            new
            {
                uid = 5,
                id = "u"
            },
            new
            {
                uid = 6,
                id = "u"
            },
            new
            {
                uid = 7,
                id = "u"
            });
        this._proxy.Setup(p => p.GetByUserAsync("raid", "u")).ReturnsAsync(json);

        Assert.Equal(7, await this._sut.CountByUserAsync("u", 1));
    }

    [Fact]
    public async Task CreateAsyncThrowsFeatureDisabledExceptionWhenGated()
    {
        this._featureGate
            .Setup(g => g.EnsureEnabledAsync(DisableFeatureKeys.Raids))
            .ThrowsAsync(new FeatureDisabledException(DisableFeatureKeys.Raids));

        var ex = await Assert.ThrowsAsync<FeatureDisabledException>(
            () => this._sut.CreateAsync("u", new Raid()));

        Assert.Equal(DisableFeatureKeys.Raids, ex.DisableKey);
        this._proxy.Verify(p => p.CreateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<JsonElement>()), Times.Never);
    }

    [Fact]
    public async Task BulkCreateAsyncThrowsFeatureDisabledExceptionWhenGated()
    {
        this._featureGate
            .Setup(g => g.EnsureEnabledAsync(DisableFeatureKeys.Raids))
            .ThrowsAsync(new FeatureDisabledException(DisableFeatureKeys.Raids));

        await Assert.ThrowsAsync<FeatureDisabledException>(
            () => this._sut.BulkCreateAsync("u", new List<Raid> { new() }));

        this._proxy.Verify(p => p.CreateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<JsonElement>()), Times.Never);
    }

    private static JsonElement CreateJsonArray(params object[] items)
    {
        var jsonStr = JsonSerializer.Serialize(items, SnakeCaseOptions);
        using var doc = JsonDocument.Parse(jsonStr);
        return doc.RootElement.Clone();
    }

    // --- Duplicate-on-edit ---
    // PoracleNG dedups raid tracking by a natural key. When an edit changes a field in that key it INSERTS
    // instead of upserting, leaving the pre-edit row behind as a second live alarm firing the old filter.

    [Fact]
    public async Task UpdateAsyncDeletesTheSupersededRowWhenPoracleNgInsertsInsteadOfUpdating()
    {
        var model = new Raid { Uid = 41 };
        this._proxy.Setup(p => p.CreateAsync("raid", "user1", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([42], 0, 0, 1));
        this._proxy.Setup(p => p.DeleteByUidAsync("raid", "user1", 41)).Returns(Task.CompletedTask);

        var result = await this._sut.UpdateAsync("user1", model);

        this._proxy.Verify(p => p.DeleteByUidAsync("raid", "user1", 41), Times.Once);
        Assert.Equal(42, result.Uid);
    }

    // A rotated uid orphans any quick pick that created the alarm: removal deletes by the stored uid,
    // finds nothing, reports success, and the alarm keeps firing. See #403.

    [Fact]
    public async Task UpdateAsyncRepointsQuickPickTrackedUidAtTheNewRow()
    {
        this._proxy.Setup(p => p.CreateAsync("raid", "user1", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([42], 0, 0, 1));

        await this._sut.UpdateAsync("user1", new Raid { Uid = 41 });

        this._uidRemapper.Verify(r => r.RemapAsync("user1", "raid", 41, 42), Times.Once);
    }

    [Fact]
    public async Task UpdateAsyncDoesNotRemapWhenTheUidSurvivesTheUpsert()
    {
        this._proxy.Setup(p => p.CreateAsync("raid", "user1", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([], 0, 1, 0));

        await this._sut.UpdateAsync("user1", new Raid { Uid = 41 });

        this._uidRemapper.Verify(
            r => r.RemapAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsyncKeepsTheUidAndDeletesNothingWhenPoracleNgUpserts()
    {
        var model = new Raid { Uid = 41 };
        this._proxy.Setup(p => p.CreateAsync("raid", "user1", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([], 0, 1, 0));

        var result = await this._sut.UpdateAsync("user1", model);

        this._proxy.Verify(p => p.DeleteByUidAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        Assert.Equal(41, result.Uid);
    }

    [Fact]
    public async Task UpdateAsyncStillSucceedsWhenDeletingTheSupersededRowFails()
    {
        var model = new Raid { Uid = 41 };
        this._proxy.Setup(p => p.CreateAsync("raid", "user1", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([42], 0, 0, 1));
        this._proxy.Setup(p => p.DeleteByUidAsync("raid", "user1", 41))
            .ThrowsAsync(new HttpRequestException("boom"));

        // The inserted row already carries the user's settings, so the edit must not fail.
        var result = await this._sut.UpdateAsync("user1", model);

        Assert.Equal(42, result.Uid);
    }

    [Fact]
    public async Task UpdateAsyncOnANewRecordDoesNotAttemptAStaleDelete()
    {
        var model = new Raid { Uid = 0 };
        this._proxy.Setup(p => p.CreateAsync("raid", "user1", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([42], 0, 0, 1));

        await this._sut.UpdateAsync("user1", model);

        this._proxy.Verify(p => p.DeleteByUidAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    // PoracleNG re-keys a row on edit while reporting insert:0 / updates:1 and putting the new uid in
    // newUids -- verified against it directly. The reconciler used to gate on Inserts > 0, so it
    // returned the DEAD uid and skipped the remap. See #460, #464.

    [Fact]
    public async Task UpdateAsyncReportsTheNewUidWhenPoracleNgReKeysWithoutReportingAnInsert()
    {
        this._proxy.Setup(p => p.CreateAsync("raid", "user1", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([418], 0, 1, 0));

        var result = await this._sut.UpdateAsync("user1", new Raid { Uid = 417 });

        Assert.Equal(418, result.Uid);
    }

    [Fact]
    public async Task UpdateAsyncRemapsQuickPickUidsWhenPoracleNgReKeysWithoutReportingAnInsert()
    {
        this._proxy.Setup(p => p.CreateAsync("raid", "user1", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([418], 0, 1, 0));

        await this._sut.UpdateAsync("user1", new Raid { Uid = 417 });

        this._uidRemapper.Verify(r => r.RemapAsync("user1", "raid", 417, 418), Times.Once);
    }

    [Fact]
    public async Task UpdateAsyncStillLeavesTheUidAloneWhenPoracleNgReturnsNoNewUid()
    {
        this._proxy.Setup(p => p.CreateAsync("raid", "user1", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([], 0, 1, 0));

        var result = await this._sut.UpdateAsync("user1", new Raid { Uid = 417 });

        Assert.Equal(417, result.Uid);
        this._uidRemapper.Verify(
            r => r.RemapAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }
}
