using System.Text.Json;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

public class GymServiceTests
{
    private static readonly JsonSerializerOptions SnakeCaseOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly Mock<IPoracleTrackingProxy> _proxy = new();
    private readonly GymService _sut;

    public GymServiceTests() => this._sut = new GymService(this._proxy.Object);

    [Fact]
    public async Task GetByUserAsyncReturnsGyms()
    {
        var json = CreateJsonArray(new { uid = 1, id = "u1" });
        this._proxy.Setup(p => p.GetByUserAsync("gym", "u1")).ReturnsAsync(json);
        Assert.Single(await this._sut.GetByUserAsync("u1", 1));
    }

    [Fact]
    public async Task GetByUidAsyncFound()
    {
        var json = CreateJsonArray(new { uid = 1, id = "u1" });
        this._proxy.Setup(p => p.GetByUserAsync("gym", "u1")).ReturnsAsync(json);
        Assert.NotNull(await this._sut.GetByUidAsync("u1", 1));
    }

    [Fact]
    public async Task GetByUidAsyncNotFound()
    {
        var json = CreateJsonArray();
        this._proxy.Setup(p => p.GetByUserAsync("gym", "u1")).ReturnsAsync(json);
        Assert.Null(await this._sut.GetByUidAsync("u1", 999));
    }

    [Fact]
    public async Task CreateAsyncSetsUserId()
    {
        this._proxy.Setup(p => p.CreateAsync("gym", "user1", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([1], 0, 0, 1));

        Assert.Equal("user1", (await this._sut.CreateAsync("user1", new Gym())).Id);
    }

    [Fact]
    public async Task DeleteAsyncTrue()
    {
        this._proxy.Setup(p => p.DeleteByUidAsync("gym", "user1", 1)).Returns(Task.CompletedTask);
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
            new { uid = 6, id = "u" },
            new { uid = 7, id = "u" });
        this._proxy.Setup(p => p.GetByUserAsync("gym", "u")).ReturnsAsync(json);
        this._proxy.Setup(p => p.BulkDeleteByUidsAsync("gym", "u", It.IsAny<IEnumerable<int>>()))
            .Returns(Task.CompletedTask);

        Assert.Equal(7, await this._sut.DeleteAllByUserAsync("u", 1));
    }

    [Fact]
    public async Task UpdateDistanceByUserAsyncCount()
    {
        var json = CreateJsonArray(
            new { uid = 1, id = "u", distance = 0 },
            new { uid = 2, id = "u", distance = 0 },
            new { uid = 3, id = "u", distance = 0 },
            new { uid = 4, id = "u", distance = 0 },
            new { uid = 5, id = "u", distance = 0 });
        this._proxy.Setup(p => p.GetByUserAsync("gym", "u")).ReturnsAsync(json);
        this._proxy.Setup(p => p.CreateAsync("gym", "u", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([], 0, 5, 0));

        Assert.Equal(5, await this._sut.UpdateDistanceByUserAsync("u", 1, 250));
    }

    [Fact]
    public async Task CountByUserAsyncCount()
    {
        var json = CreateJsonArray(Enumerable.Range(1, 11).Select(i => (object)new { uid = i, id = "u" }).ToArray());
        this._proxy.Setup(p => p.GetByUserAsync("gym", "u")).ReturnsAsync(json);

        Assert.Equal(11, await this._sut.CountByUserAsync("u", 1));
    }

    private static JsonElement CreateJsonArray(params object[] items)
    {
        var jsonStr = JsonSerializer.Serialize(items, SnakeCaseOptions);
        using var doc = JsonDocument.Parse(jsonStr);
        return doc.RootElement.Clone();
    }
}
