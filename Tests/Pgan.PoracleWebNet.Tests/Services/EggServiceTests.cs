using System.Text.Json;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

public class EggServiceTests
{
    private static readonly JsonSerializerOptions SnakeCaseOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly Mock<IPoracleTrackingProxy> _proxy = new();
    private readonly EggService _sut;

    private readonly Mock<IFeatureGate> _featureGate = new();

    public EggServiceTests()
    {
        this._featureGate.Setup(g => g.EnsureEnabledAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        this._sut = new EggService(this._proxy.Object, this._featureGate.Object);
    }

    [Fact]
    public async Task GetByUserAsyncReturnsEggs()
    {
        var json = CreateJsonArray(new
        {
            uid = 1,
            id = "u1"
        });
        this._proxy.Setup(p => p.GetByUserAsync("egg", "u1")).ReturnsAsync(json);
        Assert.Single(await this._sut.GetByUserAsync("u1", 1));
    }

    [Fact]
    public async Task GetByUidAsyncReturnsEgg()
    {
        var json = CreateJsonArray(new
        {
            uid = 1,
            id = "u1"
        });
        this._proxy.Setup(p => p.GetByUserAsync("egg", "u1")).ReturnsAsync(json);
        Assert.NotNull(await this._sut.GetByUidAsync("u1", 1));
    }

    [Fact]
    public async Task GetByUidAsyncReturnsNullWhenNotFound()
    {
        var json = CreateJsonArray();
        this._proxy.Setup(p => p.GetByUserAsync("egg", "u1")).ReturnsAsync(json);
        Assert.Null(await this._sut.GetByUidAsync("u1", 999));
    }

    [Fact]
    public async Task CreateAsyncSetsUserId()
    {
        var egg = new Egg();
        this._proxy.Setup(p => p.CreateAsync("egg", "user1", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([1], 0, 0, 1));

        var result = await this._sut.CreateAsync("user1", egg);
        Assert.Equal("user1", result.Id);
    }

    [Fact]
    public async Task UpdateAsyncCallsProxy()
    {
        var egg = new Egg { Uid = 1 };
        this._proxy.Setup(p => p.CreateAsync("egg", "user1", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([], 0, 1, 0));

        await this._sut.UpdateAsync("user1", egg);
        this._proxy.Verify(p => p.CreateAsync("egg", "user1", It.IsAny<JsonElement>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsyncReturnsTrue()
    {
        this._proxy.Setup(p => p.DeleteByUidAsync("egg", "user1", 1)).Returns(Task.CompletedTask);
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
            },
            new
            {
                uid = 4,
                id = "u"
            });
        this._proxy.Setup(p => p.GetByUserAsync("egg", "u")).ReturnsAsync(json);
        this._proxy.Setup(p => p.BulkDeleteByUidsAsync("egg", "u", It.IsAny<IEnumerable<int>>()))
            .Returns(Task.CompletedTask);

        Assert.Equal(4, await this._sut.DeleteAllByUserAsync("u", 1));
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
            },
            new
            {
                uid = 3,
                id = "u",
                distance = 0
            });
        this._proxy.Setup(p => p.GetByUserAsync("egg", "u")).ReturnsAsync(json);
        this._proxy.Setup(p => p.CreateAsync("egg", "u", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([], 0, 3, 0));

        Assert.Equal(3, await this._sut.UpdateDistanceByUserAsync("u", 1, 200));
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
            });
        this._proxy.Setup(p => p.GetByUserAsync("egg", "u")).ReturnsAsync(json);

        Assert.Equal(5, await this._sut.CountByUserAsync("u", 1));
    }

    private static JsonElement CreateJsonArray(params object[] items)
    {
        var jsonStr = JsonSerializer.Serialize(items, SnakeCaseOptions);
        using var doc = JsonDocument.Parse(jsonStr);
        return doc.RootElement.Clone();
    }
}
