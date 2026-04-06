using System.Text.Json;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

public class InvasionServiceTests
{
    private static readonly JsonSerializerOptions SnakeCaseOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly Mock<IPoracleTrackingProxy> _proxy = new();
    private readonly InvasionService _sut;

    public InvasionServiceTests() => this._sut = new InvasionService(this._proxy.Object);

    [Fact]
    public async Task GetByUserAsyncReturnsInvasions()
    {
        var json = CreateJsonArray(new { uid = 1, id = "u1" });
        this._proxy.Setup(p => p.GetByUserAsync("invasion", "u1")).ReturnsAsync(json);
        Assert.Single(await this._sut.GetByUserAsync("u1", 1));
    }

    [Fact]
    public async Task GetByUidAsyncFound()
    {
        var json = CreateJsonArray(new { uid = 1, id = "u1" });
        this._proxy.Setup(p => p.GetByUserAsync("invasion", "u1")).ReturnsAsync(json);
        Assert.NotNull(await this._sut.GetByUidAsync("u1", 1));
    }

    [Fact]
    public async Task GetByUidAsyncNotFound()
    {
        var json = CreateJsonArray();
        this._proxy.Setup(p => p.GetByUserAsync("invasion", "u1")).ReturnsAsync(json);
        Assert.Null(await this._sut.GetByUidAsync("u1", 999));
    }

    [Fact]
    public async Task CreateAsyncSetsUserId()
    {
        this._proxy.Setup(p => p.CreateAsync("invasion", "user1", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([1], 0, 0, 1));

        var result = await this._sut.CreateAsync("user1", new Invasion());
        Assert.Equal("user1", result.Id);
    }

    [Fact]
    public async Task CreateAsyncNormalizesNullGruntType()
    {
        this._proxy.Setup(p => p.CreateAsync("invasion", "u1", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([5], 0, 0, 1));

        var model = new Invasion { GruntType = null };
        var result = await this._sut.CreateAsync("u1", model);
        Assert.Equal(string.Empty, result.GruntType);
    }

    [Fact]
    public async Task UpdateAsyncDelegates()
    {
        var i = new Invasion { Uid = 1 };
        this._proxy.Setup(p => p.CreateAsync("invasion", "user1", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([], 0, 1, 0));

        await this._sut.UpdateAsync("user1", i);
        this._proxy.Verify(p => p.CreateAsync("invasion", "user1", It.IsAny<JsonElement>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsyncTrue()
    {
        this._proxy.Setup(p => p.DeleteByUidAsync("invasion", "user1", 1)).Returns(Task.CompletedTask);
        Assert.True(await this._sut.DeleteAsync("user1", 1));
    }

    [Fact]
    public async Task DeleteAllByUserAsyncCount()
    {
        var json = CreateJsonArray(
            new { uid = 1, id = "u" },
            new { uid = 2, id = "u" },
            new { uid = 3, id = "u" },
            new { uid = 4, id = "u" },
            new { uid = 5, id = "u" },
            new { uid = 6, id = "u" });
        this._proxy.Setup(p => p.GetByUserAsync("invasion", "u")).ReturnsAsync(json);
        this._proxy.Setup(p => p.BulkDeleteByUidsAsync("invasion", "u", It.IsAny<IEnumerable<int>>()))
            .Returns(Task.CompletedTask);

        Assert.Equal(6, await this._sut.DeleteAllByUserAsync("u", 1));
    }

    [Fact]
    public async Task UpdateDistanceByUserAsyncCount()
    {
        var json = CreateJsonArray(
            new { uid = 1, id = "u", distance = 0 },
            new { uid = 2, id = "u", distance = 0 },
            new { uid = 3, id = "u", distance = 0 },
            new { uid = 4, id = "u", distance = 0 });
        this._proxy.Setup(p => p.GetByUserAsync("invasion", "u")).ReturnsAsync(json);
        this._proxy.Setup(p => p.CreateAsync("invasion", "u", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([], 0, 4, 0));

        Assert.Equal(4, await this._sut.UpdateDistanceByUserAsync("u", 1, 50));
    }

    [Fact]
    public async Task CountByUserAsyncCount()
    {
        var json = CreateJsonArray(Enumerable.Range(1, 12).Select(i => (object)new { uid = i, id = "u" }).ToArray());
        this._proxy.Setup(p => p.GetByUserAsync("invasion", "u")).ReturnsAsync(json);

        Assert.Equal(12, await this._sut.CountByUserAsync("u", 1));
    }

    [Fact]
    public async Task BulkCreateAsyncSetsUserIds()
    {
        var models = new List<Invasion>
        {
            new() { GruntType = "mixed" },
            new() { GruntType = "dark" },
            new() { GruntType = null },
        };
        this._proxy.Setup(p => p.CreateAsync("invasion", "u1", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([10, 11, 12], 0, 0, 3));

        var results = (await this._sut.BulkCreateAsync("u1", models)).ToList();

        Assert.Equal(3, results.Count);
        Assert.All(results, r => Assert.Equal("u1", r.Id));
        Assert.Equal(10, results[0].Uid);
        Assert.Equal(11, results[1].Uid);
        Assert.Equal(12, results[2].Uid);
    }

    private static JsonElement CreateJsonArray(params object[] items)
    {
        var jsonStr = JsonSerializer.Serialize(items, SnakeCaseOptions);
        using var doc = JsonDocument.Parse(jsonStr);
        return doc.RootElement.Clone();
    }
}
