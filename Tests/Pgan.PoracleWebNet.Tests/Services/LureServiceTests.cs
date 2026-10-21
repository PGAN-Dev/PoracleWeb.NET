using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

public class LureServiceTests
{
    private static readonly JsonSerializerOptions SnakeCaseOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly Mock<IPoracleTrackingProxy> _proxy = new();
    private readonly Mock<IFeatureGate> _featureGate = new();
    private readonly Mock<ITrackedUidRemapper> _uidRemapper = new();
    private readonly LureService _sut;

    public LureServiceTests()
    {
        this._featureGate.Setup(g => g.EnsureEnabledAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        this._sut = new LureService(this._proxy.Object, this._featureGate.Object, NullLogger<LureService>.Instance, this._uidRemapper.Object);
        this._proxy.Setup(p => p.GetByUserAsync("lure", It.IsAny<string>()))
            .ReturnsAsync(JsonSerializer.SerializeToElement(Array.Empty<object>()));
        this._proxy.Setup(p => p.DeleteByUidAsync("lure", It.IsAny<string>(), It.IsAny<int>()))
            .Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task GetByUserAsyncReturnsLures()
    {
        var json = CreateJsonArray(new
        {
            uid = 1,
            id = "u1"
        });
        this._proxy.Setup(p => p.GetByUserAsync("lure", "u1")).ReturnsAsync(json);
        Assert.Single(await this._sut.GetByUserAsync("u1", 1));
    }

    [Fact]
    public async Task GetByUidAsyncFound()
    {
        var json = CreateJsonArray(new
        {
            uid = 1,
            id = "u1"
        });
        this._proxy.Setup(p => p.GetByUserAsync("lure", "u1")).ReturnsAsync(json);
        Assert.NotNull(await this._sut.GetByUidAsync("u1", 1));
    }

    [Fact]
    public async Task GetByUidAsyncNotFound()
    {
        var json = CreateJsonArray();
        this._proxy.Setup(p => p.GetByUserAsync("lure", "u1")).ReturnsAsync(json);
        Assert.Null(await this._sut.GetByUidAsync("u1", 999));
    }

    [Fact]
    public async Task CreateAsyncSetsUserId()
    {
        this._proxy.Setup(p => p.CreateAsync("lure", "user1", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([1], 0, 0, 1));

        Assert.Equal("user1", (await this._sut.CreateAsync("user1", new Lure())).Id);
    }

    [Fact]
    public async Task DeleteAsyncTrue()
    {
        this._proxy.Setup(p => p.DeleteByUidAsync("lure", "user1", 1)).Returns(Task.CompletedTask);
        Assert.True(await this._sut.DeleteAsync("user1", 1));
    }

    [Fact]
    public async Task DeleteAllByUserAsyncCount()
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
        this._proxy.Setup(p => p.GetByUserAsync("lure", "u")).ReturnsAsync(json);
        this._proxy.Setup(p => p.BulkDeleteByUidsAsync("lure", "u", It.IsAny<IEnumerable<int>>()))
            .Returns(Task.CompletedTask);

        Assert.Equal(3, await this._sut.DeleteAllByUserAsync("u", 1));
    }

    [Fact]
    public async Task UpdateDistanceByUserAsyncCount()
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
        this._proxy.Setup(p => p.GetByUserAsync("lure", "u")).ReturnsAsync(json);
        this._proxy.Setup(p => p.CreateAsync("lure", "u", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([], 0, 2, 0));

        Assert.Equal(2, (await this._sut.UpdateDistanceByUserAsync("u", 1, 300)).Updated);
    }

    [Fact]
    public async Task CountByUserAsyncCount()
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
        this._proxy.Setup(p => p.GetByUserAsync("lure", "u")).ReturnsAsync(json);

        Assert.Equal(4, await this._sut.CountByUserAsync("u", 1));
    }

    [Fact]
    public async Task CreateAsyncThrowsFeatureDisabledExceptionWhenGated()
    {
        this._featureGate
            .Setup(g => g.EnsureEnabledAsync(DisableFeatureKeys.Lures))
            .ThrowsAsync(new FeatureDisabledException(DisableFeatureKeys.Lures));

        var ex = await Assert.ThrowsAsync<FeatureDisabledException>(
            () => this._sut.CreateAsync("u", new Lure()));

        Assert.Equal(DisableFeatureKeys.Lures, ex.DisableKey);
        this._proxy.Verify(p => p.CreateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<JsonElement>()), Times.Never);
    }

    [Fact]
    public async Task BulkCreateAsyncThrowsFeatureDisabledExceptionWhenGated()
    {
        this._featureGate
            .Setup(g => g.EnsureEnabledAsync(DisableFeatureKeys.Lures))
            .ThrowsAsync(new FeatureDisabledException(DisableFeatureKeys.Lures));

        await Assert.ThrowsAsync<FeatureDisabledException>(
            () => this._sut.BulkCreateAsync("u", new List<Lure> { new() }));

        this._proxy.Verify(p => p.CreateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<JsonElement>()), Times.Never);
    }

    private static JsonElement CreateJsonArray(params object[] items)
    {
        var jsonStr = JsonSerializer.Serialize(items, SnakeCaseOptions);
        using var doc = JsonDocument.Parse(jsonStr);
        return doc.RootElement.Clone();
    }

    // --- Edit path ---
    // PoracleNG 5.1.0 guarded lure with a unique natural key, so an edit once had to delete the row and
    // re-create it. 5.4.0 is the minimum and migration 8 dropped the key: an edit that does not go through
    // v2 is the create-carrying-a-uid every other type uses, followed by the reconciler.

    [Fact]
    public async Task UpdateAsyncCreatesFirstAndOnlyThenDropsTheSupersededRow()
    {
        var model = new Lure { Uid = 41, LureId = 501 };
        var order = new List<string>();
        this._proxy.Setup(p => p.GetByUserAsync("lure", "user1")).ReturnsAsync(ItemsJson(41));
        this._proxy.Setup(p => p.CreateAsync("lure", "user1", It.IsAny<JsonElement>()))
            .Callback(() => order.Add("create"))
            .ReturnsAsync(new TrackingCreateResult([42], 0, 0, 1));
        this._proxy.Setup(p => p.DeleteByUidAsync("lure", "user1", 41))
            .Callback(() => order.Add("delete"))
            .Returns(Task.CompletedTask);

        var result = await this._sut.UpdateAsync("user1", model);

        Assert.Equal(42, result.Uid);
        Assert.Equal(["create", "delete"], order);
    }

    [Fact]
    public async Task UpdateAsyncLeavesTheOriginalAloneWhenTheCreateFails()
    {
        // With no delete-first there is nothing to restore: the stored rule is untouched.
        var model = new Lure { Uid = 41, LureId = 501 };
        this._proxy.Setup(p => p.GetByUserAsync("lure", "user1")).ReturnsAsync(ItemsJson(41));
        this._proxy.Setup(p => p.CreateAsync("lure", "user1", It.IsAny<JsonElement>()))
            .ThrowsAsync(new HttpRequestException("upstream 500"));

        await Assert.ThrowsAsync<HttpRequestException>(() => this._sut.UpdateAsync("user1", model));

        this._proxy.Verify(p => p.CreateAsync("lure", "user1", It.IsAny<JsonElement>()), Times.Once);
        this._proxy.Verify(p => p.DeleteByUidAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsyncOnANewRecordDoesNotDeleteAnything()
    {
        var model = new Lure { Uid = 0, LureId = 501 };
        this._proxy.Setup(p => p.CreateAsync("lure", "user1", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([44], 0, 0, 1));

        await this._sut.UpdateAsync("user1", model);

        this._proxy.Verify(p => p.DeleteByUidAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    private static JsonElement ItemsJson(int uid) =>
        JsonSerializer.SerializeToElement(new[] { new { uid, id = "user1" } });


    // A rotated uid orphans any quick pick that created the alarm. See #403.
    [Fact]
    public async Task UpdateAsyncRepointsQuickPickTrackedUidAtTheReplacementRow()
    {
        this._proxy.Setup(p => p.GetByUserAsync("lure", "user1"))
            .ReturnsAsync(CreateJsonArray(new { uid = 239, id = "user1", lure_id = 501, distance = 500 }));
        this._proxy.Setup(p => p.DeleteByUidAsync("lure", "user1", 239)).Returns(Task.CompletedTask);
        this._proxy.Setup(p => p.CreateAsync("lure", "user1", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([240], 0, 0, 1));

        var result = await this._sut.UpdateAsync("user1", new Lure { Uid = 239, LureId = 501, Distance = 600 });

        Assert.Equal(240, result.Uid);
        this._uidRemapper.Verify(r => r.RemapAsync("user1", "lure", 239, 240), Times.Once);
    }
}
