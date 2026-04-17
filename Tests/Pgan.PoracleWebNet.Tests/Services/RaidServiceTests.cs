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
    private readonly RaidService _sut;

    public RaidServiceTests()
    {
        this._featureGate.Setup(g => g.EnsureEnabledAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        this._sut = new RaidService(this._proxy.Object, this._featureGate.Object);
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

        var result = await this._sut.CreateAsync("user1", raid);

        Assert.Equal("user1", result.Id);
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

    [Fact]
    public async Task UpdateDistanceByUserAsyncReturnsCount()
    {
        var json = CreateJsonArray(
            new
            {
                uid = 1,
                id = "u",
                distance = 0
            },
            new
            {
                uid = 2,
                id = "u",
                distance = 0
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
}
