using System.Text.Json;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

public class MaxBattleServiceTests
{
    private static readonly JsonSerializerOptions SnakeCaseOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly Mock<IPoracleTrackingProxy> _proxy = new();
    private readonly MaxBattleService _sut;

    public MaxBattleServiceTests() => this._sut = new MaxBattleService(this._proxy.Object);

    [Fact]
    public async Task GetByUserAsyncReturnsMaxBattles()
    {
        var json = CreateJsonArray(new
        {
            uid = 1,
            pokemon_id = 9000,
            gmax = 1,
            station_id = "station123",
            level = 3,
            id = "user1"
        });
        this._proxy.Setup(p => p.GetByUserAsync("maxbattle", "user1")).ReturnsAsync(json);

        var result = await this._sut.GetByUserAsync("user1", 1);

        Assert.Single(result);
        Assert.Equal(3, result.First().Level);
    }

    [Fact]
    public async Task GetByUidAsyncReturnsMaxBattle()
    {
        var json = CreateJsonArray(new
        {
            uid = 1,
            pokemon_id = 9000,
            gmax = 1,
            station_id = "station123",
            id = "user1"
        });
        this._proxy.Setup(p => p.GetByUserAsync("maxbattle", "user1")).ReturnsAsync(json);

        var result = await this._sut.GetByUidAsync("user1", 1);

        Assert.NotNull(result);
        Assert.Equal(9000, result!.PokemonId);
    }

    [Fact]
    public async Task GetByUidAsyncReturnsNullWhenNotFound()
    {
        var json = CreateJsonArray();
        this._proxy.Setup(p => p.GetByUserAsync("maxbattle", "user1")).ReturnsAsync(json);

        Assert.Null(await this._sut.GetByUidAsync("user1", 999));
    }

    [Fact]
    public async Task CreateAsyncSetsUserId()
    {
        var maxBattle = new MaxBattle { PokemonId = 9000, Gmax = 1, StationId = "station123", Level = 3 };
        this._proxy.Setup(p => p.CreateAsync("maxbattle", "user1", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([1], 0, 0, 1));

        var result = await this._sut.CreateAsync("user1", maxBattle);

        Assert.Equal("user1", result.Id);
    }

    [Fact]
    public async Task UpdateAsyncUsesDeleteThenCreate()
    {
        var maxBattle = new MaxBattle { Uid = 1, PokemonId = 9000, Gmax = 1 };
        var callOrder = new List<string>();

        this._proxy.Setup(p => p.DeleteByUidAsync("maxbattle", "user1", 1))
            .Callback(() => callOrder.Add("delete"))
            .Returns(Task.CompletedTask);
        this._proxy.Setup(p => p.CreateAsync("maxbattle", "user1", It.IsAny<JsonElement>()))
            .Callback(() => callOrder.Add("create"))
            .ReturnsAsync(new TrackingCreateResult([2], 0, 0, 1));

        await this._sut.UpdateAsync("user1", maxBattle);

        this._proxy.Verify(p => p.DeleteByUidAsync("maxbattle", "user1", 1), Times.Once);
        this._proxy.Verify(p => p.CreateAsync("maxbattle", "user1", It.IsAny<JsonElement>()), Times.Once);
        Assert.Equal(2, callOrder.Count);
        Assert.Equal("delete", callOrder[0]);
        Assert.Equal("create", callOrder[1]);
    }

    [Fact]
    public async Task DeleteAsyncReturnsTrue()
    {
        this._proxy.Setup(p => p.DeleteByUidAsync("maxbattle", "user1", 1)).Returns(Task.CompletedTask);
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
        this._proxy.Setup(p => p.GetByUserAsync("maxbattle", "u")).ReturnsAsync(json);
        this._proxy.Setup(p => p.BulkDeleteByUidsAsync("maxbattle", "u", It.IsAny<IEnumerable<int>>()))
            .Returns(Task.CompletedTask);

        Assert.Equal(3, await this._sut.DeleteAllByUserAsync("u", 1));
    }

    [Fact]
    public async Task DeleteAllByUserAsyncReturnsZeroWhenEmpty()
    {
        var json = CreateJsonArray();
        this._proxy.Setup(p => p.GetByUserAsync("maxbattle", "u")).ReturnsAsync(json);

        Assert.Equal(0, await this._sut.DeleteAllByUserAsync("u", 1));
        this._proxy.Verify(p => p.BulkDeleteByUidsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<int>>()), Times.Never);
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
        this._proxy.Setup(p => p.GetByUserAsync("maxbattle", "u")).ReturnsAsync(json);
        this._proxy.Setup(p => p.BulkDeleteByUidsAsync("maxbattle", "u", It.IsAny<IEnumerable<int>>()))
            .Returns(Task.CompletedTask);
        this._proxy.Setup(p => p.CreateAsync("maxbattle", "u", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([], 0, 0, 2));

        Assert.Equal(2, await this._sut.UpdateDistanceByUserAsync("u", 1, 100));
    }

    [Fact]
    public async Task UpdateDistanceByUidsAsyncUpdatesMatchingOnly()
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
            },
            new
            {
                uid = 3,
                id = "u",
                distance = 0
            });
        this._proxy.Setup(p => p.GetByUserAsync("maxbattle", "u")).ReturnsAsync(json);
        this._proxy.Setup(p => p.BulkDeleteByUidsAsync("maxbattle", "u", It.IsAny<IEnumerable<int>>()))
            .Returns(Task.CompletedTask);
        this._proxy.Setup(p => p.CreateAsync("maxbattle", "u", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([], 0, 0, 2));

        Assert.Equal(2, await this._sut.UpdateDistanceByUidsAsync([1, 3], "u", 100));
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
        this._proxy.Setup(p => p.GetByUserAsync("maxbattle", "u")).ReturnsAsync(json);

        Assert.Equal(7, await this._sut.CountByUserAsync("u", 1));
    }

    [Fact]
    public async Task BulkCreateAsyncSetsUserIdAndAssignsUids()
    {
        var models = new List<MaxBattle>
        {
            new() { PokemonId = 9000, Gmax = 1 },
            new() { PokemonId = 9001, Gmax = 0 },
        };
        this._proxy.Setup(p => p.CreateAsync("maxbattle", "user1", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([10, 11], 0, 0, 2));

        var result = (await this._sut.BulkCreateAsync("user1", models)).ToList();

        Assert.Equal(2, result.Count);
        Assert.All(result, m => Assert.Equal("user1", m.Id));
        Assert.Equal(10, result[0].Uid);
        Assert.Equal(11, result[1].Uid);
    }

    [Fact]
    public async Task DeserializesGmaxAndStationIdFieldsCorrectly()
    {
        var json = CreateJsonArray(new
        {
            uid = 1,
            pokemon_id = 9000,
            gmax = 1,
            station_id = "station123",
            level = 3,
            id = "user1"
        });
        this._proxy.Setup(p => p.GetByUserAsync("maxbattle", "user1")).ReturnsAsync(json);

        var result = await this._sut.GetByUidAsync("user1", 1);

        Assert.NotNull(result);
        Assert.Equal(1, result!.Gmax);
        Assert.Equal("station123", result.StationId);
    }

    private static JsonElement CreateJsonArray(params object[] items)
    {
        var jsonStr = JsonSerializer.Serialize(items, SnakeCaseOptions);
        using var doc = JsonDocument.Parse(jsonStr);
        return doc.RootElement.Clone();
    }
}
