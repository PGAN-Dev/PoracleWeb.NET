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
    private readonly Mock<IPoracleIncidentProxy> _incident = new();
    private readonly InvasionService _sut;

    public InvasionServiceTests()
    {
        this._featureGate.Setup(g => g.EnsureEnabledAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        // Default: a server that serves the Pokestop Events page, so event rows belong to it.
        this._featureGate.Setup(g => g.IsEnabledAsync(It.IsAny<string>())).ReturnsAsync(true);
        this._sut = new InvasionService(this._proxy.Object, this._featureGate.Object, NullLogger<InvasionService>.Instance, this._uidRemapper.Object, this._incident.Object);
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
    public async Task UpdateAsyncPutsByUid()
    {
        var i = new Invasion { Uid = 1, GruntType = "fire" };
        this._proxy.Setup(p => p.UpdateByUidAsync("invasion", "user1", 1, It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingUpdateResult(1, true));

        await this._sut.UpdateAsync("user1", i);

        this._proxy.Verify(p => p.UpdateByUidAsync("invasion", "user1", 1, It.IsAny<JsonElement>()), Times.Once);
        this._proxy.Verify(p => p.CreateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<JsonElement>()), Times.Never);
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
        this._proxy.Setup(p => p.PatchAsync("invasion", "u", It.IsAny<IReadOnlyList<int>>(), It.IsAny<TrackingPatchSet>()))
            .ReturnsAsync((string _, string _, IReadOnlyList<int> uids, TrackingPatchSet _) => new TrackingPatchResult([.. uids], [], []));

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
            .ReturnsAsync(new TrackingCreateResult([10, 11, 12], 0, 0, 3) { RowUids = [10, 11, 12] });

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
        List<int>? sent = null;
        this._proxy.Setup(p => p.PatchAsync("invasion", "u1", It.IsAny<IReadOnlyList<int>>(), It.IsAny<TrackingPatchSet>()))
            .Callback<string, string, IReadOnlyList<int>, TrackingPatchSet>((_, _, uids, _) => sent = [.. uids])
            .ReturnsAsync((string _, string _, IReadOnlyList<int> uids, TrackingPatchSet _) => new TrackingPatchResult([.. uids], [], []));

        var count = (await this._sut.UpdateDistanceByUserAsync("u1", 1, 500)).Updated;

        Assert.Equal(2, count);
        Assert.Equal([1, 3], sent);
        this._incident.Verify(
            p => p.PatchAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<int>>(), It.IsAny<TrackingPatchSet>()), Times.Never);
    }

    [Fact]
    public async Task UpdateDistanceByUidsAsyncIgnoresAPokestopEventUid()
    {
        this.MixedTable();
        List<int>? sent = null;
        this._proxy.Setup(p => p.PatchAsync("invasion", "u1", It.IsAny<IReadOnlyList<int>>(), It.IsAny<TrackingPatchSet>()))
            .Callback<string, string, IReadOnlyList<int>, TrackingPatchSet>((_, _, uids, _) => sent = [.. uids])
            .ReturnsAsync((string _, string _, IReadOnlyList<int> uids, TrackingPatchSet _) => new TrackingPatchResult([.. uids], [], []));

        var count = (await this._sut.UpdateDistanceByUidsAsync([1, 2], "u1", 500)).Updated;

        Assert.Equal(1, count);
        Assert.Equal([1], sent);
        this._incident.Verify(
            p => p.PatchAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<int>>(), It.IsAny<TrackingPatchSet>()), Times.Never);
    }

    // ── Pokestop events written through the invasion service ────────────────
    // v2's invasion route refuses kecleon, gold-stop and showcase ("use /incident"). The invasion add
    // dialog still offers them where there is no Pokestop Events page, so they go to the incident route.

    private void IncidentCreateAnswers(Func<bool, PokestopEventWriteResult> answer) =>
        this._incident.Setup(p => p.CreateAsync("u1", It.IsAny<IEnumerable<PokestopEvent>>(), It.IsAny<bool>()))
            .ReturnsAsync((string _, IEnumerable<PokestopEvent> _, bool dryRun) => answer(dryRun));

    private static PokestopEvent Event(int uid, int displayType) => new() { Uid = uid, DisplayType = displayType };

    [Fact]
    public async Task InvasionCreateEventGruntGoesToIncident()
    {
        List<PokestopEvent>? committed = null;
        this._incident.Setup(p => p.CreateAsync("u1", It.IsAny<IEnumerable<PokestopEvent>>(), It.IsAny<bool>()))
            .Callback<string, IEnumerable<PokestopEvent>, bool>((_, rules, dryRun) =>
            {
                if (!dryRun)
                {
                    committed = [.. rules];
                }
            })
            .ReturnsAsync((string _, IEnumerable<PokestopEvent> _, bool dryRun) => dryRun
                ? new PokestopEventWriteResult([Event(0, PokestopEventTypes.Kecleon)], [], [])
                : new PokestopEventWriteResult([Event(289, PokestopEventTypes.Kecleon)], [], []));

        var result = await this._sut.CreateAsync("u1", new Invasion
        {
            GruntType = "Kecleon",
            Distance = 0,
            Template = "2",
            Clean = 1,
            OverrideAreas = ["my park"],
        });

        Assert.Equal(289, result.Uid);
        var rule = Assert.Single(committed!);
        Assert.Equal(PokestopEventTypes.Kecleon, rule.DisplayType);
        Assert.Equal("2", rule.Template);
        Assert.Equal(1, rule.Clean);
        Assert.Equal(["my park"], rule.OverrideAreas!);
        this._proxy.Verify(p => p.CreateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<JsonElement>()), Times.Never);
    }

    [Fact]
    public async Task InvasionCreateEventGruntThatWouldTakeOverIsRefused()
    {
        this.IncidentCreateAnswers(_ => new PokestopEventWriteResult([], [Event(289, PokestopEventTypes.Showcase)], []));

        var ex = await Assert.ThrowsAsync<TrackingConflictException>(
            () => this._sut.CreateAsync("u1", new Invasion { GruntType = "showcase", Distance = 900 }));

        Assert.Equal("Another alarm of this type already uses those settings. Edit or remove that one instead.", ex.Message);
        this._incident.Verify(p => p.CreateAsync("u1", It.IsAny<IEnumerable<PokestopEvent>>(), false), Times.Never);
    }

    [Fact]
    public async Task InvasionCreateEventGruntWithAGenderIsRefusedNotDropped()
    {
        var ex = await Assert.ThrowsAsync<AlarmValidationException>(
            () => this._sut.CreateAsync("u1", new Invasion { GruntType = "kecleon", Gender = 1 }));

        Assert.StartsWith("PoracleNG can't store this alarm as it is:", ex.Message, StringComparison.Ordinal);
        this._incident.Verify(p => p.CreateAsync(It.IsAny<string>(), It.IsAny<IEnumerable<PokestopEvent>>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task InvasionUpdateEventGruntGoesToIncidentReplace()
    {
        PokestopEvent? sent = null;
        this._incident.Setup(p => p.ReplaceAsync("u1", 289, It.IsAny<PokestopEvent>()))
            .Callback<string, int, PokestopEvent>((_, _, rule) => sent = rule)
            .ReturnsAsync(new PokestopEventWriteResult([], [Event(291, PokestopEventTypes.GoldStop)], []));

        var result = await this._sut.UpdateAsync("u1", new Invasion
        {
            Uid = 289,
            GruntType = "gold-stop",
            Distance = 750,
            OverrideLocationLabel = "home",
        });

        Assert.Equal(291, result.Uid);
        Assert.Equal(PokestopEventTypes.GoldStop, sent!.DisplayType);
        Assert.Equal(750, sent.Distance);
        Assert.Equal("home", sent.OverrideLocationLabel);
        this._uidRemapper.Verify(r => r.RemapAsync("u1", "invasion", 289, 291), Times.Once);
        this._proxy.Verify(p => p.UpdateByUidAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<JsonElement>()), Times.Never);
        this._proxy.Verify(p => p.CreateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<JsonElement>()), Times.Never);
    }

    [Fact]
    public async Task BulkCreateRoutesEventRowsToIncidentAndTheRestToInvasion()
    {
        this.IncidentCreateAnswers(dryRun => dryRun
            ? new PokestopEventWriteResult([Event(0, PokestopEventTypes.Showcase)], [], [])
            : new PokestopEventWriteResult([Event(300, PokestopEventTypes.Showcase)], [], []));
        JsonElement? invasionBody = null;
        this._proxy.Setup(p => p.CreateAsync("invasion", "u1", It.IsAny<JsonElement>()))
            .Callback<string, string, JsonElement>((_, _, b) => invasionBody = b)
            .ReturnsAsync(new TrackingCreateResult([10, 11], 0, 0, 2) { RowUids = [10, 11] });

        var results = (await this._sut.BulkCreateAsync("u1", [
            new Invasion { GruntType = "water" },
            new Invasion { GruntType = "showcase" },
            new Invasion { GruntType = "fire" },
        ])).ToList();

        Assert.Equal([10, 300, 11], results.Select(r => r.Uid));
        Assert.DoesNotContain("showcase", invasionBody!.Value.GetRawText(), StringComparison.Ordinal);
    }

    // ── metal is steel ──────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsyncSendsMetalAsSteel()
    {
        JsonElement? body = null;
        this._proxy.Setup(p => p.CreateAsync("invasion", "u1", It.IsAny<JsonElement>()))
            .Callback<string, string, JsonElement>((_, _, b) => body = b)
            .ReturnsAsync(new TrackingCreateResult([1], 0, 0, 1) { RowUids = [1] });

        await this._sut.CreateAsync("u1", new Invasion { GruntType = "metal", Gender = 2 });

        Assert.Equal("steel", body!.Value.GetProperty("grunt_type").GetString());
    }

    [Fact]
    public async Task UpdateAsyncRepairsALegacyMetalRuleToSteel()
    {
        // Stored rules still say metal. v2 refuses that name, so an edit that kept it would fail on a rule
        // the user did not break; it is saved under the name the game data uses instead.
        JsonElement? body = null;
        this._proxy.Setup(p => p.UpdateByUidAsync("invasion", "u1", 4, It.IsAny<JsonElement>()))
            .Callback<string, string, int, JsonElement>((_, _, _, b) => body = b)
            .ReturnsAsync(new TrackingUpdateResult(4, true));

        await this._sut.UpdateAsync("u1", new Invasion { Uid = 4, GruntType = "metal", Distance = 100 });

        Assert.Equal("steel", body!.Value.GetProperty("grunt_type").GetString());
    }

    [Fact]
    public async Task CreateAsyncLeavesOtherGruntNamesAlone()
    {
        JsonElement? body = null;
        this._proxy.Setup(p => p.CreateAsync("invasion", "u1", It.IsAny<JsonElement>()))
            .Callback<string, string, JsonElement>((_, _, b) => body = b)
            .ReturnsAsync(new TrackingCreateResult([1], 0, 0, 1) { RowUids = [1] });

        await this._sut.CreateAsync("u1", new Invasion { GruntType = "npc_3" });

        Assert.Equal("npc_3", body!.Value.GetProperty("grunt_type").GetString());
    }

    // ---- a kind change is a create then a delete, never a write addressed by the old uid (#1062) ----
    //
    // These run with the Pokestop Events page off (disable_showcase): with it on, the controller refuses an
    // invasion edited into an event before the service is reached.

    private void EventsPageOff() => this._featureGate.Setup(g => g.IsEnabledAsync(DisableFeatureKeys.PokestopEvents)).ReturnsAsync(false);

    private void SharedUid7() => this._proxy.Setup(p => p.GetByUserAsync("invasion", "u1")).ReturnsAsync(CreateJsonArray(
        new { uid = 7, id = "u1", grunt_type = "water" },
        new { uid = 7, id = "u1", grunt_type = "kecleon" }));

    [Fact]
    public async Task EditingAnInvasionIntoAnEventLeavesAnUnrelatedEventThatSharesItsUidAlone()
    {
        this.EventsPageOff();
        this.IncidentCreateAnswers(dryRun => dryRun
            ? new PokestopEventWriteResult([Event(0, PokestopEventTypes.GoldStop)], [], [])
            : new PokestopEventWriteResult([Event(300, PokestopEventTypes.GoldStop)], [], []));

        var result = await this._sut.UpdateAsync("u1", new Invasion { Uid = 7, GruntType = "gold-stop", Distance = 500 }, "invasion");

        Assert.Equal(300, result.Uid);
        this._incident.Verify(p => p.ReplaceAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<PokestopEvent>()), Times.Never);
        this._incident.Verify(p => p.DeleteByUidAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        this._proxy.Verify(p => p.DeleteByUidAsync("invasion", "u1", 7), Times.Once);
        this._uidRemapper.Verify(r => r.RemapAsync("u1", "invasion", 7, 300), Times.Once);
        // Nothing is read for the decision: the request said which kind it opened.
        this._proxy.Verify(p => p.GetByUserAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task EditingAnEventIntoAGruntLeavesAnUnrelatedGruntThatSharesItsUidAlone()
    {
        this.EventsPageOff();
        this._proxy.Setup(p => p.CreateAsync("invasion", "u1", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([12], 0, 0, 1) { RowUids = [12] });

        var result = await this._sut.UpdateAsync("u1", new Invasion { Uid = 7, GruntType = "water" }, "event");

        Assert.Equal(12, result.Uid);
        this._incident.Verify(p => p.DeleteByUidAsync("u1", 7), Times.Once);
        this._proxy.Verify(p => p.UpdateByUidAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<JsonElement>()), Times.Never);
        this._proxy.Verify(p => p.DeleteByUidAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task AKindChangeThatTheCreateRefusesLeavesTheOldRuleInPlace()
    {
        this.EventsPageOff();
        this.IncidentCreateAnswers(_ => throw new TrackingConflictException("incident", "conflict"));

        await Assert.ThrowsAsync<TrackingConflictException>(
            () => this._sut.UpdateAsync("u1", new Invasion { Uid = 7, GruntType = "kecleon" }, "invasion"));

        this._proxy.Verify(p => p.DeleteByUidAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task AKindChangeWhoseCreateFindsAnIdenticalRuleStillRemovesTheOldOneAndAnswersUidZero()
    {
        this.EventsPageOff();
        this.IncidentCreateAnswers(dryRun => dryRun
            ? new PokestopEventWriteResult([], [], [Event(0, PokestopEventTypes.Kecleon)])
            : throw new InvalidOperationException("an unchanged rule is not written"));

        var result = await this._sut.UpdateAsync("u1", new Invasion { Uid = 7, GruntType = "kecleon" }, "invasion");

        Assert.Equal(0, result.Uid);
        this._proxy.Verify(p => p.DeleteByUidAsync("invasion", "u1", 7), Times.Once);
        this._uidRemapper.Verify(r => r.RemapAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task AnEventEditedAsAnEventStaysInPlaceOnTheIncidentRoute()
    {
        this.EventsPageOff();
        this._incident.Setup(p => p.ReplaceAsync("u1", 7, It.IsAny<PokestopEvent>()))
            .ReturnsAsync(new PokestopEventWriteResult([], [Event(7, PokestopEventTypes.Kecleon)], []));

        var result = await this._sut.UpdateAsync("u1", new Invasion { Uid = 7, GruntType = "kecleon" }, "event");

        Assert.Equal(7, result.Uid);
        this._incident.Verify(p => p.ReplaceAsync("u1", 7, It.IsAny<PokestopEvent>()), Times.Once);
        this._proxy.Verify(p => p.DeleteByUidAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task APlainInvasionEditStillUpdatesInPlaceWithTheEventsPageOn()
    {
        this._proxy.Setup(p => p.UpdateByUidAsync("invasion", "u1", 7, It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingUpdateResult(7, true));

        var result = await this._sut.UpdateAsync("u1", new Invasion { Uid = 7, GruntType = "fire" }, "invasion");

        Assert.Equal(7, result.Uid);
        this._proxy.Verify(p => p.UpdateByUidAsync("invasion", "u1", 7, It.IsAny<JsonElement>()), Times.Once);
        this._proxy.Verify(p => p.CreateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<JsonElement>()), Times.Never);
        this._proxy.Verify(p => p.DeleteByUidAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task WithoutAStatedKindAnEventEditKeepsTheOldInPlaceBehaviour()
    {
        this._incident.Setup(p => p.ReplaceAsync("u1", 7, It.IsAny<PokestopEvent>()))
            .ReturnsAsync(new PokestopEventWriteResult([], [Event(7, PokestopEventTypes.Kecleon)], []));

        await this._sut.UpdateAsync("u1", new Invasion { Uid = 7, GruntType = "kecleon" });

        this._incident.Verify(p => p.ReplaceAsync("u1", 7, It.IsAny<PokestopEvent>()), Times.Once);
        this._proxy.Verify(p => p.DeleteByUidAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Theory]
    [InlineData("invasion", "water")]
    [InlineData("event", "kecleon")]
    public async Task TheLookupPicksTheRowOfTheOpenedKindWhenBothShareAUid(string kind, string expectedGrunt)
    {
        this.EventsPageOff();
        this.SharedUid7();

        var found = await this._sut.GetByUidAsync("u1", 7, kind);

        Assert.Equal(expectedGrunt, found!.GruntType);
    }

    [Fact]
    public async Task ALookupWithoutAKindStillTakesTheFirstMatch()
    {
        this.EventsPageOff();
        this.SharedUid7();

        Assert.Equal("water", (await this._sut.GetByUidAsync("u1", 7, null))!.GruntType);
    }

    [Fact]
    public async Task AFailedReadDuringTheLookupPropagatesInsteadOfFallingBackToAReplacingWrite()
    {
        this._proxy.Setup(p => p.GetByUserAsync("invasion", "u1")).ThrowsAsync(new HttpRequestException("down"));

        await Assert.ThrowsAsync<HttpRequestException>(() => this._sut.GetByUidAsync("u1", 7, "invasion"));
        this._incident.Verify(p => p.ReplaceAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<PokestopEvent>()), Times.Never);
    }
}
