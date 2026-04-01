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
    private readonly MonsterService _sut;

    public MonsterServiceTests() => this._sut = new MonsterService(this._proxy.Object);

    [Fact]
    public async Task GetByUserAsyncReturnsMonsters()
    {
        var json = CreateJsonArray(new { uid = 1, pokemon_id = 25, id = "user1" });
        this._proxy.Setup(p => p.GetByUserAsync("pokemon", "user1")).ReturnsAsync(json);

        var result = await this._sut.GetByUserAsync("user1", 1);

        Assert.Single(result);
        Assert.Equal(25, result.First().PokemonId);
    }

    [Fact]
    public async Task GetByUidAsyncReturnsMonster()
    {
        var json = CreateJsonArray(new { uid = 1, pokemon_id = 25, id = "user1" });
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
        var createResult = new TrackingCreateResult([], 0, 1, 0);
        this._proxy.Setup(p => p.CreateAsync("pokemon", "user1", It.IsAny<JsonElement>()))
            .ReturnsAsync(createResult);

        var result = await this._sut.UpdateAsync("user1", monster);

        Assert.Equal(1, result.Uid);
        this._proxy.Verify(p => p.CreateAsync("pokemon", "user1", It.IsAny<JsonElement>()), Times.Once);
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
            new { uid = 1, pokemon_id = 25, id = "user1" },
            new { uid = 2, pokemon_id = 150, id = "user1" });
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
            new { uid = 1, pokemon_id = 25, id = "user1", distance = 0 },
            new { uid = 2, pokemon_id = 150, id = "user1", distance = 0 },
            new { uid = 3, pokemon_id = 6, id = "user1", distance = 0 });
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
            new { uid = 1, pokemon_id = 25, id = "user1" },
            new { uid = 2, pokemon_id = 150, id = "user1" });
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
            new { uid = 1, pokemon_id = 25, id = "user1", distance = 0 },
            new { uid = 2, pokemon_id = 150, id = "user1", distance = 0 },
            new { uid = 3, pokemon_id = 6, id = "user1", distance = 0 });
        this._proxy.Setup(p => p.GetByUserAsync("pokemon", "user1")).ReturnsAsync(json);
        this._proxy.Setup(p => p.CreateAsync("pokemon", "user1", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([], 0, 2, 0));

        var result = await this._sut.UpdateDistanceByUidsAsync([1, 3], "user1", 500);

        Assert.Equal(2, result);
    }

    private static JsonElement CreateJsonArray(params object[] items)
    {
        var jsonStr = JsonSerializer.Serialize(items, SnakeCaseOptions);
        using var doc = JsonDocument.Parse(jsonStr);
        return doc.RootElement.Clone();
    }
}
