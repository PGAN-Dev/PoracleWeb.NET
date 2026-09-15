using System.Text.Json;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

public class MonsterServiceTests
{
    private static readonly JsonSerializerOptions SnakeCaseOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly Mock<IPoracleTrackingProxy> _proxy = new();
    private readonly Mock<IFeatureGate> _featureGate = new();
    private readonly Mock<ITrackedUidRemapper> _remapper = new();
    private readonly MonsterService _sut;

    public MonsterServiceTests()
    {
        this._featureGate.Setup(g => g.EnsureEnabledAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        this._sut = new MonsterService(this._proxy.Object, this._featureGate.Object, this._remapper.Object, CostumeCapabilityDoubles.Supported());
    }

    [Fact]
    public async Task GetByUserAsyncReturnsMonsters()
    {
        var json = CreateJsonArray(new
        {
            uid = 1,
            pokemon_id = 25,
            id = "user1"
        });
        this._proxy.Setup(p => p.GetByUserAsync("pokemon", "user1")).ReturnsAsync(json);

        var result = await this._sut.GetByUserAsync("user1", 1);

        Assert.Single(result);
        Assert.Equal(25, result.First().PokemonId);
    }

    [Fact]
    public async Task GetByUidAsyncReturnsMonster()
    {
        var json = CreateJsonArray(new
        {
            uid = 1,
            pokemon_id = 25,
            id = "user1"
        });
        this._proxy.Setup(p => p.GetByUserAsync("pokemon", "user1")).ReturnsAsync(json);

        var result = await this._sut.GetByUidAsync("user1", 1);

        Assert.NotNull(result);
        Assert.Equal(25, result!.PokemonId);
    }

    [Fact]
    public async Task GetByUidAsyncReturnsNullWhenNotFound()
    {
        var json = CreateJsonArray();
        this._proxy.Setup(p => p.GetByUserAsync("pokemon", "user1")).ReturnsAsync(json);

        var result = await this._sut.GetByUidAsync("user1", 999);

        Assert.Null(result);
    }

    [Fact]
    public async Task CreateAsyncSetsUserIdAndCallsProxy()
    {
        var monster = new Monster { PokemonId = 25 };
        var createResult = new TrackingCreateResult([42], 0, 0, 1);
        this._proxy.Setup(p => p.CreateAsync("pokemon", "user1", It.IsAny<JsonElement>()))
            .ReturnsAsync(createResult);

        var result = await this._sut.CreateAsync("user1", monster);

        Assert.Equal("user1", result.Id);
        Assert.Equal(42, result.Uid);
        this._proxy.Verify(p => p.CreateAsync("pokemon", "user1", It.IsAny<JsonElement>()), Times.Once);
    }

    [Fact]
    public async Task UpdateAsyncCallsProxy()
    {
        var monster = new Monster { Uid = 1, PokemonId = 25 };
        this._proxy
            .Setup(p => p.UpdateByUidAsync("pokemon", "user1", 1, It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingUpdateResult(1, false));

        var result = await this._sut.UpdateAsync("user1", monster);

        Assert.Equal(1, result.Uid);
        this._proxy.Verify(
            p => p.UpdateByUidAsync("pokemon", "user1", 1, It.IsAny<JsonElement>()), Times.Once);
    }

    /// <summary>
    /// The v2 PUT is delete-then-insert, so the rule comes back under a new uid. Anything holding the old
    /// one has to follow it -- quick-pick applied state above all, whose "remove" button otherwise deletes
    /// nothing and then wipes itself. See #403 and #805.
    /// </summary>
    [Fact]
    public async Task UpdateAsyncFollowsARotatedUid()
    {
        var monster = new Monster { Uid = 36486, PokemonId = 25 };
        this._proxy
            .Setup(p => p.UpdateByUidAsync("pokemon", "user1", 36486, It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingUpdateResult(36487, true));

        var result = await this._sut.UpdateAsync("user1", monster);

        Assert.Equal(36487, result.Uid);
        this._remapper.Verify(r => r.RemapAsync("user1", "pokemon", 36486, 36487), Times.Once);
    }

    /// <summary>
    /// The legitimate case that must still pass: on the v1 surface the uid does not move, and nothing
    /// should be remapped. A remap fired on every save would rewrite applied state for no reason.
    /// </summary>
    [Fact]
    public async Task UpdateAsyncDoesNotRemapWhenTheUidStays()
    {
        var monster = new Monster { Uid = 7, PokemonId = 25 };
        this._proxy
            .Setup(p => p.UpdateByUidAsync("pokemon", "user1", 7, It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingUpdateResult(7, false));

        var result = await this._sut.UpdateAsync("user1", monster);

        Assert.Equal(7, result.Uid);
        this._remapper.Verify(
            r => r.RemapAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()),
            Times.Never);
    }

    /// <summary>
    /// A proxy that named no uid -- an unparseable 200, or a v1 response carrying no newUids -- must leave
    /// the alarm where it was rather than moving it to 0.
    /// </summary>
    [Fact]
    public async Task UpdateAsyncKeepsItsUidWhenPoracleNamesNone()
    {
        var monster = new Monster { Uid = 7, PokemonId = 25 };
        this._proxy
            .Setup(p => p.UpdateByUidAsync("pokemon", "user1", 7, It.IsAny<JsonElement>()))
            .ReturnsAsync(default(TrackingUpdateResult));

        var result = await this._sut.UpdateAsync("user1", monster);

        Assert.Equal(7, result.Uid);
    }

    /// <summary>
    /// The legitimate case #553 was about, kept alive across the surface change. Two alarms that differ by
    /// more than one updatable field genuinely coexist upstream, so an edit onto that shape must still
    /// save. On v1 the guard early-returns for pokemon (#606); on v2 the uid-addressed PUT cannot merge at
    /// all, and PoracleNG answers 409 itself if the result would be an exact duplicate.
    /// </summary>
    [Fact]
    public async Task UpdateAsyncStillSavesWhenAnotherAlarmIsMerelySimilar()
    {
        var stored = CreateJsonArray(
            new { uid = 1, id = "user1", pokemon_id = 25, distance = 1000, min_iv = 90, template = "1" },
            new { uid = 2, id = "user1", pokemon_id = 25, distance = 4000, min_iv = 90, template = "2" });
        this._proxy.Setup(p => p.GetByUserAsync("pokemon", "user1")).ReturnsAsync(stored);
        this._proxy
            .Setup(p => p.UpdateByUidAsync("pokemon", "user1", 1, It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingUpdateResult(1, false));

        // Both distance and template now match the sibling: two updatable differences, not one.
        var result = await this._sut.UpdateAsync(
            "user1", new Monster { Uid = 1, Id = "user1", PokemonId = 25, Distance = 4000, MinIv = 90, Template = "2" });

        Assert.Equal(1, result.Uid);
        this._proxy.Verify(
            p => p.UpdateByUidAsync("pokemon", "user1", 1, It.IsAny<JsonElement>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsyncCallsProxy()
    {
        this._proxy.Setup(p => p.DeleteByUidAsync("pokemon", "user1", 1)).Returns(Task.CompletedTask);

        var result = await this._sut.DeleteAsync("user1", 1);

        Assert.True(result);
        this._proxy.Verify(p => p.DeleteByUidAsync("pokemon", "user1", 1), Times.Once);
    }

    [Fact]
    public async Task DeleteAllByUserAsyncReturnsCount()
    {
        var json = CreateJsonArray(
            new
            {
                uid = 1,
                pokemon_id = 25,
                id = "user1"
            },
            new
            {
                uid = 2,
                pokemon_id = 150,
                id = "user1"
            });
        this._proxy.Setup(p => p.GetByUserAsync("pokemon", "user1")).ReturnsAsync(json);
        this._proxy.Setup(p => p.BulkDeleteByUidsAsync("pokemon", "user1", It.IsAny<IEnumerable<int>>()))
            .Returns(Task.CompletedTask);

        var result = await this._sut.DeleteAllByUserAsync("user1", 1);

        Assert.Equal(2, result);
    }

    [Fact]
    public async Task DeleteAllByUserAsyncReturnsZeroWhenEmpty()
    {
        var json = CreateJsonArray();
        this._proxy.Setup(p => p.GetByUserAsync("pokemon", "user1")).ReturnsAsync(json);

        var result = await this._sut.DeleteAllByUserAsync("user1", 1);

        Assert.Equal(0, result);
        this._proxy.Verify(p => p.BulkDeleteByUidsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<int>>()), Times.Never);
    }

    [Fact]
    public async Task UpdateDistanceByUserAsyncReturnsCount()
    {
        var json = CreateJsonArray(
            new
            {
                uid = 1,
                pokemon_id = 25,
                id = "user1",
                distance = 0
            },
            new
            {
                uid = 2,
                pokemon_id = 150,
                id = "user1",
                distance = 0
            },
            new
            {
                uid = 3,
                pokemon_id = 6,
                id = "user1",
                distance = 0
            });
        this._proxy.Setup(p => p.GetByUserAsync("pokemon", "user1")).ReturnsAsync(json);
        this._proxy.Setup(p => p.CreateAsync("pokemon", "user1", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([], 0, 3, 0));

        var result = await this._sut.UpdateDistanceByUserAsync("user1", 1, 500);

        Assert.Equal(3, result);
    }

    [Fact]
    public async Task CountByUserAsyncReturnsCount()
    {
        var json = CreateJsonArray(
            new
            {
                uid = 1,
                pokemon_id = 25,
                id = "user1"
            },
            new
            {
                uid = 2,
                pokemon_id = 150,
                id = "user1"
            });
        this._proxy.Setup(p => p.GetByUserAsync("pokemon", "user1")).ReturnsAsync(json);

        var result = await this._sut.CountByUserAsync("user1", 1);

        Assert.Equal(2, result);
    }

    [Fact]
    public async Task BulkCreateAsyncSetsUserIdAndAssignsUids()
    {
        var monsters = new List<Monster>
        {
            new() { PokemonId = 25 },
            new() { PokemonId = 150 },
        };
        var createResult = new TrackingCreateResult([10, 11], 0, 0, 2);
        this._proxy.Setup(p => p.CreateAsync("pokemon", "user1", It.IsAny<JsonElement>()))
            .ReturnsAsync(createResult);

        var result = (await this._sut.BulkCreateAsync("user1", monsters)).ToList();

        Assert.Equal(2, result.Count);
        Assert.Equal("user1", result[0].Id);
        Assert.Equal("user1", result[1].Id);
        Assert.Equal(10, result[0].Uid);
        Assert.Equal(11, result[1].Uid);
    }

    [Fact]
    public async Task UpdateDistanceByUidsAsyncUpdatesMatchingOnly()
    {
        var json = CreateJsonArray(
            new
            {
                uid = 1,
                pokemon_id = 25,
                id = "user1",
                distance = 0
            },
            new
            {
                uid = 2,
                pokemon_id = 150,
                id = "user1",
                distance = 0
            },
            new
            {
                uid = 3,
                pokemon_id = 6,
                id = "user1",
                distance = 0
            });
        this._proxy.Setup(p => p.GetByUserAsync("pokemon", "user1")).ReturnsAsync(json);
        this._proxy.Setup(p => p.CreateAsync("pokemon", "user1", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([], 0, 2, 0));

        var result = await this._sut.UpdateDistanceByUidsAsync([1, 3], "user1", 500);

        Assert.Equal(2, result);
    }

    [Fact]
    public async Task CreateAsyncThrowsFeatureDisabledExceptionWhenGated()
    {
        // Service-layer guard — closes the bypass class found in the iteration-1 review where
        // QuickPickService.Apply, ProfileController.Duplicate, and ProfileOverviewController.ImportProfile
        // call MonsterService.CreateAsync directly without going through the controller filter. (#236)
        this._featureGate
            .Setup(g => g.EnsureEnabledAsync(DisableFeatureKeys.Pokemon))
            .ThrowsAsync(new FeatureDisabledException(DisableFeatureKeys.Pokemon));

        var ex = await Assert.ThrowsAsync<FeatureDisabledException>(
            () => this._sut.CreateAsync("user1", new Monster { PokemonId = 25 }));

        Assert.Equal(DisableFeatureKeys.Pokemon, ex.DisableKey);
        // Must short-circuit before the proxy call — otherwise we leak a row to PoracleNG.
        this._proxy.Verify(p => p.CreateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<JsonElement>()), Times.Never);
    }

    [Fact]
    public async Task BulkCreateAsyncThrowsFeatureDisabledExceptionWhenGated()
    {
        this._featureGate
            .Setup(g => g.EnsureEnabledAsync(DisableFeatureKeys.Pokemon))
            .ThrowsAsync(new FeatureDisabledException(DisableFeatureKeys.Pokemon));

        await Assert.ThrowsAsync<FeatureDisabledException>(
            () => this._sut.BulkCreateAsync("user1", new List<Monster> { new() { PokemonId = 25 } }));

        this._proxy.Verify(p => p.CreateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<JsonElement>()), Times.Never);
    }

    private static JsonElement CreateJsonArray(params object[] items)
    {
        var jsonStr = JsonSerializer.Serialize(items, SnakeCaseOptions);
        using var doc = JsonDocument.Parse(jsonStr);
        return doc.RootElement.Clone();
    }
}
