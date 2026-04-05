using System.Text.Json;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

public class CleaningServiceTests
{
    private readonly Mock<IPoracleTrackingProxy> _proxy = new();
    private readonly CleaningService _sut;

    public CleaningServiceTests() => this._sut = new CleaningService(this._proxy.Object);

    [Fact]
    public async Task ToggleCleanMonstersAsyncUpdatesAllAlarms()
    {
        var json = CreateJsonArray(
            new
            {
                uid = 1,
                clean = 0
            },
            new
            {
                uid = 2,
                clean = 0
            },
            new
            {
                uid = 3,
                clean = 0
            },
            new
            {
                uid = 4,
                clean = 0
            },
            new
            {
                uid = 5,
                clean = 0
            });
        this._proxy.Setup(p => p.GetByUserAsync("pokemon", "u1")).ReturnsAsync(json);
        this._proxy.Setup(p => p.CreateAsync("pokemon", "u1", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([], 0, 5, 0));

        Assert.Equal(5, await this._sut.ToggleCleanMonstersAsync("u1", 1, 1));
    }

    [Fact]
    public async Task ToggleCleanRaidsAsyncUpdatesAllAlarms()
    {
        var json = CreateJsonArray(
            new
            {
                uid = 1,
                clean = 1
            },
            new
            {
                uid = 2,
                clean = 1
            },
            new
            {
                uid = 3,
                clean = 1
            });
        this._proxy.Setup(p => p.GetByUserAsync("raid", "u1")).ReturnsAsync(json);
        this._proxy.Setup(p => p.CreateAsync("raid", "u1", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([], 0, 3, 0));

        Assert.Equal(3, await this._sut.ToggleCleanRaidsAsync("u1", 1, 0));
    }

    [Fact]
    public async Task ToggleCleanEggsAsyncUpdatesAllAlarms()
    {
        var json = CreateJsonArray(new
        {
            uid = 1,
            clean = 0
        }, new
        {
            uid = 2,
            clean = 0
        });
        this._proxy.Setup(p => p.GetByUserAsync("egg", "u1")).ReturnsAsync(json);
        this._proxy.Setup(p => p.CreateAsync("egg", "u1", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([], 0, 2, 0));

        Assert.Equal(2, await this._sut.ToggleCleanEggsAsync("u1", 1, 1));
    }

    [Fact]
    public async Task ToggleCleanQuestsAsyncUpdatesAllAlarms()
    {
        var json = CreateJsonArray(
            new
            {
                uid = 1,
                clean = 0
            },
            new
            {
                uid = 2,
                clean = 0
            },
            new
            {
                uid = 3,
                clean = 0
            },
            new
            {
                uid = 4,
                clean = 0
            });
        this._proxy.Setup(p => p.GetByUserAsync("quest", "u1")).ReturnsAsync(json);
        this._proxy.Setup(p => p.CreateAsync("quest", "u1", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([], 0, 4, 0));

        Assert.Equal(4, await this._sut.ToggleCleanQuestsAsync("u1", 1, 1));
    }

    [Fact]
    public async Task ToggleCleanInvasionsAsyncUpdatesAllAlarms()
    {
        var json = CreateJsonArray(
            new
            {
                uid = 1,
                clean = 1
            },
            new
            {
                uid = 2,
                clean = 1
            },
            new
            {
                uid = 3,
                clean = 1
            },
            new
            {
                uid = 4,
                clean = 1
            },
            new
            {
                uid = 5,
                clean = 1
            },
            new
            {
                uid = 6,
                clean = 1
            });
        this._proxy.Setup(p => p.GetByUserAsync("invasion", "u1")).ReturnsAsync(json);
        this._proxy.Setup(p => p.CreateAsync("invasion", "u1", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([], 0, 6, 0));

        Assert.Equal(6, await this._sut.ToggleCleanInvasionsAsync("u1", 1, 0));
    }

    [Fact]
    public async Task ToggleCleanLuresAsyncUpdatesAllAlarms()
    {
        var json = CreateJsonArray(new
        {
            uid = 1,
            clean = 0
        });
        this._proxy.Setup(p => p.GetByUserAsync("lure", "u1")).ReturnsAsync(json);
        this._proxy.Setup(p => p.CreateAsync("lure", "u1", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([], 0, 1, 0));

        Assert.Equal(1, await this._sut.ToggleCleanLuresAsync("u1", 1, 1));
    }

    [Fact]
    public async Task ToggleCleanNestsAsyncUpdatesAllAlarms()
    {
        var json = CreateJsonArray([.. Enumerable.Range(1, 8).Select(i => (object)new { uid = i, clean = 0 })]);
        this._proxy.Setup(p => p.GetByUserAsync("nest", "u1")).ReturnsAsync(json);
        this._proxy.Setup(p => p.CreateAsync("nest", "u1", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([], 0, 8, 0));

        Assert.Equal(8, await this._sut.ToggleCleanNestsAsync("u1", 1, 1));
    }

    [Fact]
    public async Task ToggleCleanGymsAsyncUpdatesAllAlarms()
    {
        var json = CreateJsonArray([.. Enumerable.Range(1, 9).Select(i => (object)new { uid = i, clean = 1 })]);
        this._proxy.Setup(p => p.GetByUserAsync("gym", "u1")).ReturnsAsync(json);
        this._proxy.Setup(p => p.CreateAsync("gym", "u1", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([], 0, 9, 0));

        Assert.Equal(9, await this._sut.ToggleCleanGymsAsync("u1", 1, 0));
    }

    [Fact]
    public async Task ToggleCleanMaxBattlesAsyncUpdatesAllAlarms()
    {
        var json = CreateJsonArray(
            new
            {
                uid = 1,
                clean = 0
            },
            new
            {
                uid = 2,
                clean = 0
            },
            new
            {
                uid = 3,
                clean = 0
            });
        this._proxy.Setup(p => p.GetByUserAsync("maxbattle", "u1")).ReturnsAsync(json);
        this._proxy.Setup(p => p.CreateAsync("maxbattle", "u1", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([], 0, 3, 0));

        Assert.Equal(3, await this._sut.ToggleCleanMaxBattlesAsync("u1", 1, 1));
    }

    [Fact]
    public async Task ToggleCleanReturnsZeroWhenNoAlarms()
    {
        var json = CreateJsonArray();
        this._proxy.Setup(p => p.GetByUserAsync("pokemon", "u1")).ReturnsAsync(json);

        Assert.Equal(0, await this._sut.ToggleCleanMonstersAsync("u1", 1, 1));
        this._proxy.Verify(p => p.CreateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<JsonElement>()), Times.Never);
    }

    [Fact]
    public async Task GetCleanStatusAsyncReturnsTrueWhenAllClean()
    {
        var json = CreateAllTrackingJson(cleanValue: 1, countPerType: 2);
        this._proxy.Setup(p => p.GetAllTrackingAsync("u1")).ReturnsAsync(json);

        var result = await this._sut.GetCleanStatusAsync("u1", 1);

        Assert.True(result["monsters"]);
        Assert.True(result["raids"]);
        Assert.True(result["maxbattles"]);
    }

    [Fact]
    public async Task GetCleanStatusAsyncReturnsFalseWhenNotAllClean()
    {
        // Mix of clean=0 and clean=1
        var obj = new Dictionary<string, object[]>
        {
            ["pokemon"] = [new { uid = 1, clean = 1 }, new { uid = 2, clean = 0 }],
            ["raid"] = [new { uid = 1, clean = 1 }],
            ["egg"] = [],
            ["quest"] = [],
            ["invasion"] = [],
            ["lure"] = [],
            ["nest"] = [],
            ["gym"] = [],
            ["maxbattle"] = [new { uid = 1, clean = 1 }, new { uid = 2, clean = 1 }],
        };
        var jsonStr = JsonSerializer.Serialize(obj);
        using var doc = JsonDocument.Parse(jsonStr);
        var json = doc.RootElement.Clone();
        this._proxy.Setup(p => p.GetAllTrackingAsync("u1")).ReturnsAsync(json);

        var result = await this._sut.GetCleanStatusAsync("u1", 1);

        Assert.False(result["monsters"]); // one is not clean
        Assert.True(result["raids"]);     // single item is clean
        Assert.False(result["eggs"]);     // empty array
        Assert.True(result["maxbattles"]); // both are clean
    }

    private static JsonElement CreateJsonArray(params object[] items)
    {
        var jsonStr = JsonSerializer.Serialize(items);
        using var doc = JsonDocument.Parse(jsonStr);
        return doc.RootElement.Clone();
    }

    private static JsonElement CreateAllTrackingJson(int cleanValue, int countPerType)
    {
        var types = new[] { "pokemon", "raid", "egg", "quest", "invasion", "lure", "nest", "gym", "maxbattle" };
        var obj = new Dictionary<string, object[]>();
        foreach (var type in types)
        {
            obj[type] = [.. Enumerable.Range(1, countPerType).Select(i => (object)new { uid = i, clean = cleanValue })];
        }

        var jsonStr = JsonSerializer.Serialize(obj);
        using var doc = JsonDocument.Parse(jsonStr);
        return doc.RootElement.Clone();
    }
}
