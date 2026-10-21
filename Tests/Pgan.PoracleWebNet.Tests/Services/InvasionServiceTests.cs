using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
    private readonly Mock<IFeatureGate> _featureGate = new();
    private readonly Mock<ITrackedUidRemapper> _uidRemapper = new();
    private readonly InvasionService _sut;

    public InvasionServiceTests()
    {
        this._featureGate.Setup(g => g.EnsureEnabledAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        // Default: a server that serves the Pokestop Events page, so event rows belong to it.
        this._featureGate.Setup(g => g.IsEnabledAsync(It.IsAny<string>())).ReturnsAsync(true);
        this._sut = new InvasionService(this._proxy.Object, this._featureGate.Object, NullLogger<InvasionService>.Instance, this._uidRemapper.Object);
        this._proxy.Setup(p => p.GetByUserAsync("invasion", It.IsAny<string>()))
            .ReturnsAsync(JsonSerializer.SerializeToElement(Array.Empty<object>()));
        this._proxy.Setup(p => p.DeleteByUidAsync("invasion", It.IsAny<string>(), It.IsAny<int>()))
            .Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task GetByUserAsyncReturnsInvasions()
    {
        var json = CreateJsonArray(new
        {
            uid = 1,
            id = "u1"
        });
        this._proxy.Setup(p => p.GetByUserAsync("invasion", "u1")).ReturnsAsync(json);
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

        var result = await this._sut.CreateAsync("user1", new Invasion { GruntType = "fire" });
        Assert.Equal("user1", result.Id);
    }

    [Fact]
    public async Task UpdateAsyncDelegates()
    {
        var i = new Invasion { Uid = 1, GruntType = "fire" };
        // PoracleNG re-keys the edited rule, so a new uid comes back and the reconciler drops the old one.
        this._proxy.Setup(p => p.CreateAsync("invasion", "user1", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([2], 0, 0, 1));

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
            });
        this._proxy.Setup(p => p.GetByUserAsync("invasion", "u")).ReturnsAsync(json);
        this._proxy.Setup(p => p.BulkDeleteByUidsAsync("invasion", "u", It.IsAny<IEnumerable<int>>()))
            .Returns(Task.CompletedTask);

        Assert.Equal(6, await this._sut.DeleteAllByUserAsync("u", 1));
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
            },
            new
            {
                uid = 3,
                id = "u",
                distance = 0,
                template = "ZZrow3"
            },
            new
            {
                uid = 4,
                id = "u",
                distance = 0,
                template = "ZZrow4"
            });
        this._proxy.Setup(p => p.GetByUserAsync("invasion", "u")).ReturnsAsync(json);
        this._proxy.Setup(p => p.CreateAsync("invasion", "u", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([], 0, 4, 0));

        Assert.Equal(4, (await this._sut.UpdateDistanceByUserAsync("u", 1, 50)).Updated);
    }

    [Fact]
    public async Task CountByUserAsyncCount()
    {
        var json = CreateJsonArray([.. Enumerable.Range(1, 12).Select(i => (object)new { uid = i, id = "u" })]);
        this._proxy.Setup(p => p.GetByUserAsync("invasion", "u")).ReturnsAsync(json);

        Assert.Equal(12, await this._sut.CountByUserAsync("u", 1));
    }

    // These two used to assert that a missing grunt type is coalesced to "". That was the bug:
    // PoracleNG rejects an empty grunt_type with 400 "Grunt type mandatory" and has no catch-all,
    // so the coalesce guaranteed a failure that reached the user as a generic 500. See #416.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateAsyncRejectsMissingGruntTypeInsteadOfSendingItUpstream(string? gruntType)
    {
        this._proxy.Setup(p => p.CreateAsync("invasion", "u1", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([1], 0, 0, 1));

        await Assert.ThrowsAsync<AlarmValidationException>(
            () => this._sut.CreateAsync("u1", new Invasion { GruntType = gruntType }));

        this._proxy.Verify(p => p.CreateAsync("invasion", "u1", It.IsAny<JsonElement>()), Times.Never);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task UpdateAsyncRejectsMissingGruntType(string? gruntType)
    {
        await Assert.ThrowsAsync<AlarmValidationException>(
            () => this._sut.UpdateAsync("u1", new Invasion { Uid = 1, GruntType = gruntType }));
    }

    [Fact]
    public async Task BulkCreateAsyncRejectsMissingGruntTypeWithoutPartiallyCreating()
    {
        var models = new List<Invasion>
        {
            new() { GruntType = "fire" },
            new() { GruntType = null },
        };

        await Assert.ThrowsAsync<AlarmValidationException>(() => this._sut.BulkCreateAsync("u1", models));

        this._proxy.Verify(p => p.CreateAsync("invasion", "u1", It.IsAny<JsonElement>()), Times.Never);
    }

    [Fact]
    public async Task BulkCreateAsyncSetsUserIds()
    {
        var models = new List<Invasion>
        {
            new() { GruntType = "mixed" },
            new() { GruntType = "dark" },
            new() { GruntType = "giovanni" },
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

    [Fact]
    public async Task CreateAsyncThrowsFeatureDisabledExceptionWhenGated()
    {
        this._featureGate
            .Setup(g => g.EnsureEnabledAsync(DisableFeatureKeys.Invasions))
            .ThrowsAsync(new FeatureDisabledException(DisableFeatureKeys.Invasions));

        var ex = await Assert.ThrowsAsync<FeatureDisabledException>(
            () => this._sut.CreateAsync("u", new Invasion()));

        Assert.Equal(DisableFeatureKeys.Invasions, ex.DisableKey);
        this._proxy.Verify(p => p.CreateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<JsonElement>()), Times.Never);
    }

    [Fact]
    public async Task BulkCreateAsyncThrowsFeatureDisabledExceptionWhenGated()
    {
        this._featureGate
            .Setup(g => g.EnsureEnabledAsync(DisableFeatureKeys.Invasions))
            .ThrowsAsync(new FeatureDisabledException(DisableFeatureKeys.Invasions));

        await Assert.ThrowsAsync<FeatureDisabledException>(
            () => this._sut.BulkCreateAsync("u", new List<Invasion> { new() }));

        this._proxy.Verify(p => p.CreateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<JsonElement>()), Times.Never);
    }

    private static JsonElement CreateJsonArray(params object[] items)
    {
        var jsonStr = JsonSerializer.Serialize(items, SnakeCaseOptions);
        using var doc = JsonDocument.Parse(jsonStr);
        return doc.RootElement.Clone();
    }

    // --- Edit path ---
    // PoracleNG 5.1.0 guarded invasion with a unique natural key, so an edit once had to delete the row and
    // re-create it. 5.4.0 is the minimum and migration 8 dropped the key: an edit that does not go through
    // v2 is the create-carrying-a-uid every other type uses, followed by the reconciler.

    [Fact]
    public async Task UpdateAsyncCreatesFirstAndOnlyThenDropsTheSupersededRow()
    {
        var model = new Invasion { Uid = 41, GruntType = "grunt" };
        var order = new List<string>();
        this._proxy.Setup(p => p.GetByUserAsync("invasion", "user1")).ReturnsAsync(ItemsJson(41));
        this._proxy.Setup(p => p.CreateAsync("invasion", "user1", It.IsAny<JsonElement>()))
            .Callback(() => order.Add("create"))
            .ReturnsAsync(new TrackingCreateResult([42], 0, 0, 1));
        this._proxy.Setup(p => p.DeleteByUidAsync("invasion", "user1", 41))
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
        var model = new Invasion { Uid = 41, GruntType = "grunt" };
        this._proxy.Setup(p => p.GetByUserAsync("invasion", "user1")).ReturnsAsync(ItemsJson(41));
        this._proxy.Setup(p => p.CreateAsync("invasion", "user1", It.IsAny<JsonElement>()))
            .ThrowsAsync(new HttpRequestException("upstream 500"));

        await Assert.ThrowsAsync<HttpRequestException>(() => this._sut.UpdateAsync("user1", model));

        this._proxy.Verify(p => p.CreateAsync("invasion", "user1", It.IsAny<JsonElement>()), Times.Once);
        this._proxy.Verify(p => p.DeleteByUidAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsyncOnANewRecordDoesNotDeleteAnything()
    {
        var model = new Invasion { Uid = 0, GruntType = "grunt" };
        this._proxy.Setup(p => p.CreateAsync("invasion", "user1", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([44], 0, 0, 1));

        await this._sut.UpdateAsync("user1", model);

        this._proxy.Verify(p => p.DeleteByUidAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    private static JsonElement ItemsJson(int uid) =>
        JsonSerializer.SerializeToElement(new[] { new { uid, id = "user1" } });

    [Theory]
    [InlineData("fi\u0000re")]
    [InlineData("fire\u001bx")]
    [InlineData("fire\n")]
    public async Task CreateAsyncRefusesControlCharactersInGruntType(string gruntType)
    {
        var ex = await Assert.ThrowsAsync<AlarmValidationException>(
            () => this._sut.CreateAsync("user1", new Invasion { GruntType = gruntType }));

        Assert.Contains("control characters", ex.Message, StringComparison.OrdinalIgnoreCase);
        this._proxy.Verify(p => p.CreateAsync("invasion", It.IsAny<string>(), It.IsAny<JsonElement>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsyncRefusesAGruntTypeLongerThanTheColumn()
    {
        // varchar(255) upstream. This asserted 35, an invented limit the fix's own rationale forbade.
        // See #661.
        var ex = await Assert.ThrowsAsync<AlarmValidationException>(
            () => this._sut.CreateAsync("user1", new Invasion { GruntType = new string('a', 256) }));

        Assert.Contains("255", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateAsyncAcceptsAGruntTypeTheColumnCanHold()
    {
        // The legitimate-case-still-passes half. See #661.
        this._proxy.Setup(p => p.CreateAsync("invasion", "user1", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([12], 0, 0, 1));

        var result = await this._sut.CreateAsync("user1", new Invasion { GruntType = new string('a', 255) });

        Assert.Equal(12, result.Uid);
    }

    // ── the shared invasion table: incident rows are not invasion rows ───────

    /// <summary>
    /// A mixed table as PoracleNG serves it over v1: two invasions and a showcase, with the showcase
    /// row indistinguishable except by its grunt_type.
    /// </summary>
    private void MixedTable() =>
        this._proxy.Setup(p => p.GetByUserAsync("invasion", "u1")).ReturnsAsync(CreateJsonArray(
            new { uid = 1, id = "u1", grunt_type = "water", distance = 100 },
            new { uid = 2, id = "u1", grunt_type = "showcase", distance = 100 },
            new { uid = 3, id = "u1", grunt_type = "everything", distance = 100 }));

    [Fact]
    public async Task PokestopEventRowsAreNotListedAsInvasions()
    {
        this.MixedTable();

        var items = await this._sut.GetByUserAsync("u1", 1);

        Assert.Equal([1, 3], items.Select(x => x.Uid));
    }

    /// <summary>
    /// The legitimate-case-still-passes half, and the one that matters most. On a PoracleNG too old
    /// to serve the Pokestop Events page — or with <c>disable_showcase</c> set — the page that would
    /// hold these rows does not exist, so hiding them here would leave alarms that fire and cannot be
    /// seen or deleted. Event rows are creatable from the invasion add dialog on 5.1.0 today.
    /// </summary>
    [Fact]
    public async Task PokestopEventRowsStayInTheInvasionListWhenThereIsNowhereElseForThemToGo()
    {
        this._featureGate.Setup(g => g.IsEnabledAsync(DisableFeatureKeys.PokestopEvents)).ReturnsAsync(false);
        this.MixedTable();

        var items = await this._sut.GetByUserAsync("u1", 1);

        Assert.Equal([1, 2, 3], items.Select(x => x.Uid));
    }

    [Fact]
    public async Task CountByUserAsyncCountsOnlyInvasions()
    {
        this.MixedTable();

        Assert.Equal(2, await this._sut.CountByUserAsync("u1", 1));
    }

    [Fact]
    public async Task GetByUidAsyncDoesNotHandOutAPokestopEventRow()
    {
        this.MixedTable();

        Assert.Null(await this._sut.GetByUidAsync("u1", 2));
        Assert.NotNull(await this._sut.GetByUidAsync("u1", 1));
    }

    [Fact]
    public async Task DeleteAllByUserAsyncLeavesPokestopEventRowsAlone()
    {
        this.MixedTable();
        List<int>? deleted = null;
        this._proxy.Setup(p => p.BulkDeleteByUidsAsync("invasion", "u1", It.IsAny<IEnumerable<int>>()))
            .Callback<string, string, IEnumerable<int>>((_, _, uids) => deleted = [.. uids])
            .Returns(Task.CompletedTask);

        var count = await this._sut.DeleteAllByUserAsync("u1", 1);

        Assert.Equal(2, count);
        Assert.Equal([1, 3], deleted);
    }

    [Fact]
    public async Task UpdateDistanceByUserAsyncDoesNotRewritePokestopEventRows()
    {
        this.MixedTable();
        JsonElement? body = null;
        this._proxy.Setup(p => p.CreateAsync("invasion", "u1", It.IsAny<JsonElement>()))
            .Callback<string, string, JsonElement>((_, _, b) => body = b)
            .ReturnsAsync(new TrackingCreateResult([], 0, 2, 0));

        var count = (await this._sut.UpdateDistanceByUserAsync("u1", 1, 500)).Updated;

        Assert.Equal(2, count);
        Assert.DoesNotContain(
            "showcase",
            body!.Value.GetRawText(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateDistanceByUidsAsyncIgnoresAPokestopEventUid()
    {
        this.MixedTable();
        JsonElement? body = null;
        this._proxy.Setup(p => p.CreateAsync("invasion", "u1", It.IsAny<JsonElement>()))
            .Callback<string, string, JsonElement>((_, _, b) => body = b)
            .ReturnsAsync(new TrackingCreateResult([], 0, 1, 0));

        var count = (await this._sut.UpdateDistanceByUidsAsync([1, 2], "u1", 500)).Updated;

        Assert.Equal(1, count);
        Assert.DoesNotContain("showcase", body!.Value.GetRawText(), StringComparison.Ordinal);
    }
}
