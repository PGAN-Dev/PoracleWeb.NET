using System.Text.Json;
using Microsoft.Extensions.Logging;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

public class CleaningServiceTests
{
    private readonly Mock<IPoracleTrackingProxy> _proxy = new();
    private readonly Mock<IFeatureGate> _featureGate = new();
    private readonly Mock<IPoracleIncidentProxy> _incident = new();
    private readonly List<(string Type, List<int> Uids, TrackingPatchSet Set)> _patched = [];
    private readonly List<(List<int> Uids, TrackingPatchSet Set)> _incidentPatched = [];
    private readonly CleaningService _sut;

    public CleaningServiceTests()
    {
        this._featureGate.Setup(g => g.EnsureEnabledAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        this._proxy
            .Setup(p => p.PatchAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<int>>(), It.IsAny<TrackingPatchSet>()))
            .ReturnsAsync((string type, string _, IReadOnlyList<int> uids, TrackingPatchSet set) =>
            {
                this._patched.Add((type, [.. uids], set));
                return new TrackingPatchResult([.. uids], [], []);
            });
        this._incident
            .Setup(p => p.PatchAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<int>>(), It.IsAny<TrackingPatchSet>()))
            .ReturnsAsync((string _, IReadOnlyList<int> uids, TrackingPatchSet set) =>
            {
                this._incidentPatched.Add(([.. uids], set));
                return new TrackingPatchResult([.. uids], [], []);
            });
        this._sut = new CleaningService(
            this._proxy.Object,
            this._featureGate.Object,
            this._incident.Object,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CleaningService>.Instance);
    }

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

        Assert.Equal(1, await this._sut.ToggleCleanLuresAsync("u1", 1, 1));
    }

    [Fact]
    public async Task ToggleCleanNestsAsyncUpdatesAllAlarms()
    {
        var json = CreateJsonArray([.. Enumerable.Range(1, 8).Select(i => (object)new { uid = i, clean = 0 })]);
        this._proxy.Setup(p => p.GetByUserAsync("nest", "u1")).ReturnsAsync(json);

        Assert.Equal(8, await this._sut.ToggleCleanNestsAsync("u1", 1, 1));
    }

    [Fact]
    public async Task ToggleCleanGymsAsyncUpdatesAllAlarms()
    {
        var json = CreateJsonArray([.. Enumerable.Range(1, 9).Select(i => (object)new { uid = i, clean = 1 })]);
        this._proxy.Setup(p => p.GetByUserAsync("gym", "u1")).ReturnsAsync(json);

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

        Assert.Equal(3, await this._sut.ToggleCleanMaxBattlesAsync("u1", 1, 1));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    public async Task CleaningToggleIsOnePatchPerTypeKeepsOtherBits(int clean, bool expected)
    {
        // clean=5 = auto-delete (bit 1) + summary (bit 4); clean=2 = edit-in-place only (bit 2). The toggle owns
        // bit 1 alone, so the PATCH names clean and nothing else: PoracleNG leaves every field it is not told
        // about as stored, which is what keeps bot-set bits 2 and 4. (#292)
        this._proxy.Setup(p => p.GetByUserAsync("pokemon", "u1")).ReturnsAsync(CreateJsonArray(
            new { uid = 1, clean = 5 },
            new { uid = 2, clean = 2 }));

        Assert.Equal(2, await this._sut.ToggleCleanMonstersAsync("u1", 1, clean));

        var (type, uids, set) = Assert.Single(this._patched);
        Assert.Equal("pokemon", type);
        Assert.Equal([1, 2], uids);
        Assert.Equal(new TrackingPatchSet(Clean: expected), set);
        Assert.Null(set.Distance);
        this._proxy.Verify(
            p => p.CreateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<JsonElement>()), Times.Never);
        this._proxy.Verify(
            p => p.UpdateByUidAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<JsonElement>()),
            Times.Never);
    }

    [Fact]
    public async Task CleaningToggleCountsUpdatedAndUnchangedButNotRefused()
    {
        this._proxy.Setup(p => p.GetByUserAsync("raid", "u1")).ReturnsAsync(CreateJsonArray(
            new { uid = 1, clean = 0 },
            new { uid = 2, clean = 1 },
            new { uid = 3, clean = 0 }));
        this._proxy
            .Setup(p => p.PatchAsync("raid", "u1", It.IsAny<IReadOnlyList<int>>(), It.IsAny<TrackingPatchSet>()))
            .ReturnsAsync(new TrackingPatchResult(
                [1], [2], [new PatchRefusal(3, PatchRefusalReasons.Conflict, "duplicate", 1)]));

        Assert.Equal(2, await this._sut.ToggleCleanRaidsAsync("u1", 1, 1));
    }

    [Fact]
    public async Task CleaningToggleWarnsAboutEveryRuleItLeftAlone()
    {
        // The count already leaves a refused rule out, so the switch looks fine to the user. The log is the only
        // place that says which rules kept their old setting and why.
        this._proxy.Setup(p => p.GetByUserAsync("raid", "u1")).ReturnsAsync(CreateJsonArray(
            new { uid = 1, clean = 0 },
            new { uid = 3, clean = 0 }));
        this._proxy
            .Setup(p => p.PatchAsync("raid", "u1", It.IsAny<IReadOnlyList<int>>(), It.IsAny<TrackingPatchSet>()))
            .ReturnsAsync(new TrackingPatchResult(
                [1], [], [new PatchRefusal(3, PatchRefusalReasons.Invalid, "area not allowed", null)]));
        var logger = new CapturingLogger();
        var sut = new CleaningService(this._proxy.Object, this._featureGate.Object, this._incident.Object, logger);

        Assert.Equal(1, await sut.ToggleCleanRaidsAsync("u1", 1, 1));

        var warning = Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains("uid=3", warning.Message, StringComparison.Ordinal);
        Assert.Contains("invalid", warning.Message, StringComparison.Ordinal);
        Assert.Contains("area not allowed", warning.Message, StringComparison.Ordinal);
    }

    private sealed class CapturingLogger : ILogger<CleaningService>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            this.Entries.Add((logLevel, formatter(state, exception)));
    }

    [Fact]
    public async Task GetCleanStatusAsyncTreatsMultiBitValuesAsClean()
    {
        // clean=3 (auto-delete+edit) and clean=5 (auto-delete+summary) both have bit 1 set,
        // so AllClean must report them as clean even though they are not exactly == 1. (#292)
        var obj = new Dictionary<string, object[]>
        {
            ["pokemon"] = [new { uid = 1, clean = 3 }, new { uid = 2, clean = 5 }],
            ["raid"] = [new { uid = 1, clean = 5 }],
            ["egg"] = [],
            ["quest"] = [],
            ["invasion"] = [],
            ["lure"] = [],
            ["nest"] = [],
            ["gym"] = [],
            ["maxbattle"] = [new { uid = 1, clean = 3 }],
        };
        var jsonStr = JsonSerializer.Serialize(obj);
        using var doc = JsonDocument.Parse(jsonStr);
        var json = doc.RootElement.Clone();
        this._proxy.Setup(p => p.GetAllTrackingAsync("u1")).ReturnsAsync(json);

        var result = await this._sut.GetCleanStatusAsync("u1", 1);

        Assert.True(result["monsters"]);   // clean=3 and clean=5 both have bit 1
        Assert.True(result["raids"]);      // clean=5 has bit 1
        Assert.True(result["maxbattles"]); // clean=3 has bit 1
    }

    [Fact]
    public async Task GetCleanStatusAsyncTreatsBitlessValueAsNotClean()
    {
        // clean=4 = summary only (bit 1 not set) -> not auto-delete -> not clean. (#292)
        var obj = new Dictionary<string, object[]>
        {
            ["pokemon"] = [new { uid = 1, clean = 4 }],
            ["raid"] = [],
            ["egg"] = [],
            ["quest"] = [],
            ["invasion"] = [],
            ["lure"] = [],
            ["nest"] = [],
            ["gym"] = [],
            ["maxbattle"] = [],
        };
        var jsonStr = JsonSerializer.Serialize(obj);
        using var doc = JsonDocument.Parse(jsonStr);
        var json = doc.RootElement.Clone();
        this._proxy.Setup(p => p.GetAllTrackingAsync("u1")).ReturnsAsync(json);

        var result = await this._sut.GetCleanStatusAsync("u1", 1);

        Assert.False(result["monsters"]); // clean=4 lacks the auto-delete bit
    }

    [Fact]
    public async Task ToggleCleanReturnsZeroWhenNoAlarms()
    {
        var json = CreateJsonArray();
        this._proxy.Setup(p => p.GetByUserAsync("pokemon", "u1")).ReturnsAsync(json);

        Assert.Equal(0, await this._sut.ToggleCleanMonstersAsync("u1", 1, 1));
        Assert.Empty(this._patched);
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

    [Theory]
    [InlineData("pokemon", DisableFeatureKeys.Pokemon)]
    [InlineData("raid", DisableFeatureKeys.Raids)]
    [InlineData("egg", DisableFeatureKeys.Raids)]
    [InlineData("quest", DisableFeatureKeys.Quests)]
    [InlineData("invasion", DisableFeatureKeys.Invasions)]
    [InlineData("lure", DisableFeatureKeys.Lures)]
    [InlineData("nest", DisableFeatureKeys.Nests)]
    [InlineData("gym", DisableFeatureKeys.Gyms)]
    [InlineData("maxbattle", DisableFeatureKeys.MaxBattles)]
    public async Task ToggleCleanThrowsFeatureDisabledExceptionPerType(string trackingType, string disableKey)
    {
        // Iteration 2 review surfaced that CleaningService bypassed the alarm-service gates by writing
        // through the proxy directly. This test guards every type. (#236)
        this._featureGate
            .Setup(g => g.EnsureEnabledAsync(disableKey))
            .ThrowsAsync(new FeatureDisabledException(disableKey));

        var task = trackingType switch
        {
            "pokemon" => this._sut.ToggleCleanMonstersAsync("u1", 1, 1),
            "raid" => this._sut.ToggleCleanRaidsAsync("u1", 1, 1),
            "egg" => this._sut.ToggleCleanEggsAsync("u1", 1, 1),
            "quest" => this._sut.ToggleCleanQuestsAsync("u1", 1, 1),
            "invasion" => this._sut.ToggleCleanInvasionsAsync("u1", 1, 1),
            "lure" => this._sut.ToggleCleanLuresAsync("u1", 1, 1),
            "nest" => this._sut.ToggleCleanNestsAsync("u1", 1, 1),
            "gym" => this._sut.ToggleCleanGymsAsync("u1", 1, 1),
            "maxbattle" => this._sut.ToggleCleanMaxBattlesAsync("u1", 1, 1),
            _ => throw new InvalidOperationException(trackingType),
        };

        var ex = await Assert.ThrowsAsync<FeatureDisabledException>(() => task);
        Assert.Equal(disableKey, ex.DisableKey);
        // Must short-circuit before any proxy call — otherwise we'd flush a write to PoracleNG even
        // though the feature is disabled.
        this._proxy.Verify(p => p.GetByUserAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        Assert.Empty(this._patched);
    }

    // --- Max Battles duplicated on every toggle (#402) ---
    // PoracleNG's v1 maxbattle create was insert-only, so the old fetch-modify-POST had to delete the rows
    // first and restore them if the re-create failed. PATCH updates in place, so neither is needed.

    [Fact]
    public async Task ToggleCleanMaxBattlesPatchesInPlaceWithoutDeleting()
    {
        var json = JsonSerializer.SerializeToElement(new[]
        {
            new { uid = 82, id = "u1", clean = 0 },
            new { uid = 83, id = "u1", clean = 0 },
        });
        this._proxy.Setup(p => p.GetByUserAsync("maxbattle", "u1")).ReturnsAsync(json);

        var count = await this._sut.ToggleCleanMaxBattlesAsync("u1", 1, 1);

        Assert.Equal(2, count);
        Assert.Equal([82, 83], Assert.Single(this._patched).Uids);
        this._proxy.Verify(
            p => p.BulkDeleteByUidsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<int>>()), Times.Never);
    }

    [Fact]
    public async Task ToggleCleanMonstersNeverDeletes()
    {
        var json = JsonSerializer.SerializeToElement(new[] { new { uid = 5, id = "u1", clean = 0 } });
        this._proxy.Setup(p => p.GetByUserAsync("pokemon", "u1")).ReturnsAsync(json);

        await this._sut.ToggleCleanMonstersAsync("u1", 1, 1);

        this._proxy.Verify(p => p.BulkDeleteByUidsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<int>>()), Times.Never);
    }

    [Fact]
    public async Task StatusNoLongerReportsFortChanges()
    {
        this._proxy.Setup(p => p.GetAllTrackingAsync("u1"))
            .ReturnsAsync(JsonSerializer.SerializeToElement(new { }));

        var status = await this._sut.GetCleanStatusAsync("u1", 1);

        // forts carry no clean column in PoracleNG, so the entry was always a meaningless false.
        Assert.DoesNotContain("fortChanges", status.Keys);
        Assert.DoesNotContain("fortchanges", status.Keys);
    }


    /// <summary>
    /// Kecleon, Showcase and Gold Stop rules live in the invasion table but belong to the Pokestop
    /// Events page. The invasion clean switch used to rewrite them too.
    /// </summary>
    [Fact]
    public async Task ToggleCleanInvasionsLeavesPokestopEventRowsAlone()
    {
        this._featureGate.Setup(g => g.IsEnabledAsync(DisableFeatureKeys.PokestopEvents)).ReturnsAsync(true);
        this._proxy.Setup(p => p.GetByUserAsync("invasion", "u1")).ReturnsAsync(CreateJsonArray(
            new { uid = 1, clean = 0, grunt_type = "water" },
            new { uid = 2, clean = 0, grunt_type = "kecleon" },
            new { uid = 3, clean = 0, grunt_type = "showcase" }));

        Assert.Equal(1, await this._sut.ToggleCleanInvasionsAsync("u1", 1, 1));
        Assert.Equal([1], Assert.Single(this._patched).Uids);
        Assert.Empty(this._incidentPatched);
    }

    /// <summary>
    /// The legitimate case the partition must not break: with the Pokestop Events surface switched off
    /// there is no other page holding those rows, so the invasion switch still owns them.
    /// </summary>
    [Fact]
    public async Task ToggleCleanInvasionsIncludesEventRowsWhenThatPageIsOff()
    {
        this._featureGate.Setup(g => g.IsEnabledAsync(DisableFeatureKeys.PokestopEvents)).ReturnsAsync(false);
        this._proxy.Setup(p => p.GetByUserAsync("invasion", "u1")).ReturnsAsync(CreateJsonArray(
            new { uid = 1, clean = 0, grunt_type = "water" },
            new { uid = 2, clean = 0, grunt_type = "kecleon" }));

        // v2's invasion route does not hold the event rules, so they go to incident.
        Assert.Equal(2, await this._sut.ToggleCleanInvasionsAsync("u1", 1, 1));
        var (type, uids, set) = Assert.Single(this._patched);
        Assert.Equal("invasion", type);
        Assert.Equal([1], uids);
        Assert.Equal(new TrackingPatchSet(Clean: true), set);
        var (eventUids, eventSet) = Assert.Single(this._incidentPatched);
        Assert.Equal([2], eventUids);
        Assert.Equal(new TrackingPatchSet(Clean: true), eventSet);
    }

    [Fact]
    public async Task ToggleCleanInvasionsWritesNothingWhenOnlyEventRowsExist()
    {
        this._featureGate.Setup(g => g.IsEnabledAsync(DisableFeatureKeys.PokestopEvents)).ReturnsAsync(true);
        this._proxy.Setup(p => p.GetByUserAsync("invasion", "u1")).ReturnsAsync(CreateJsonArray(
            new { uid = 2, clean = 0, grunt_type = "kecleon" }));

        Assert.Equal(0, await this._sut.ToggleCleanInvasionsAsync("u1", 1, 1));
        Assert.Empty(this._patched);
        Assert.Empty(this._incidentPatched);
    }

    [Fact]
    public async Task GetCleanStatusIgnoresPokestopEventRowsForInvasions()
    {
        this._featureGate.Setup(g => g.IsEnabledAsync(DisableFeatureKeys.PokestopEvents)).ReturnsAsync(true);
        var json = JsonSerializer.SerializeToElement(new
        {
            invasion = new object[]
            {
                new { uid = 1, clean = 1, grunt_type = "water" },
                new { uid = 2, clean = 0, grunt_type = "showcase" },
            },
        });
        this._proxy.Setup(p => p.GetAllTrackingAsync("u1")).ReturnsAsync(json);

        Assert.True((await this._sut.GetCleanStatusAsync("u1", 1))["invasions"]);
    }

    [Fact]
    public async Task GetCleanStatusReportsNotCleanWhenOnlyEventRowsExist()
    {
        this._featureGate.Setup(g => g.IsEnabledAsync(DisableFeatureKeys.PokestopEvents)).ReturnsAsync(true);
        var json = JsonSerializer.SerializeToElement(new
        {
            invasion = new object[] { new { uid = 2, clean = 1, grunt_type = "showcase" } },
        });
        this._proxy.Setup(p => p.GetAllTrackingAsync("u1")).ReturnsAsync(json);

        Assert.False((await this._sut.GetCleanStatusAsync("u1", 1))["invasions"]);
    }

    // Alarms D10: one clean rule among several read as "off", with nothing to say part of the type was on.
    [Fact]
    public async Task GetCleanCountsAsyncCountsCleanRulesPerType()
    {
        var json = JsonSerializer.SerializeToElement(new
        {
            pokemon = new object[] { new { uid = 1, clean = 1 }, new { uid = 2, clean = 0 }, new { uid = 3, clean = 0 } },
            raid = new object[] { new { uid = 1, clean = 5 } },
            egg = Array.Empty<object>(),
            maxbattle = new object[] { new { uid = 1, clean = 2 }, new { uid = 2, clean = 1 } },
        });
        this._proxy.Setup(p => p.GetAllTrackingAsync("u1")).ReturnsAsync(json);

        var counts = await this._sut.GetCleanCountsAsync("u1", 1);

        Assert.Equal(new CleanCount(1, 3), counts["monsters"]);
        Assert.Equal(new CleanCount(1, 1), counts["raids"]);
        Assert.Equal(new CleanCount(0, 0), counts["eggs"]);
        Assert.Equal(new CleanCount(0, 0), counts["quests"]); // absent from the snapshot
        Assert.Equal(new CleanCount(1, 2), counts["maxbattles"]); // edit-in-place alone is not auto-delete
    }

    [Fact]
    public async Task GetCleanCountsAsyncLeavesPokestopEventRowsOutOfInvasions()
    {
        this._featureGate.Setup(g => g.IsEnabledAsync(DisableFeatureKeys.PokestopEvents)).ReturnsAsync(true);
        var json = JsonSerializer.SerializeToElement(new
        {
            invasion = new object[]
            {
                new { uid = 1, clean = 1, grunt_type = "water" },
                new { uid = 2, clean = 0, grunt_type = "fire" },
                new { uid = 3, clean = 1, grunt_type = "showcase" },
            },
        });
        this._proxy.Setup(p => p.GetAllTrackingAsync("u1")).ReturnsAsync(json);

        Assert.Equal(new CleanCount(1, 2), (await this._sut.GetCleanCountsAsync("u1", 1))["invasions"]);
    }

    [Fact]
    public async Task GetCleanCountsAsyncCountsBooleanTrueAsCleanAndFalseNullOrStringAsNotRatherThanThrowing()
    {
        var json = JsonSerializer.SerializeToElement(new
        {
            pokemon = new object[] { new { uid = 1, clean = true } },
            raid = new object?[] { new { uid = 1, clean = false }, new { uid = 2, clean = (int?)null }, new { uid = 3, clean = "1" } },
        });
        this._proxy.Setup(p => p.GetAllTrackingAsync("u1")).ReturnsAsync(json);

        var counts = await this._sut.GetCleanCountsAsync("u1", 1);
        var status = await this._sut.GetCleanStatusAsync("u1", 1);

        Assert.Equal(new CleanCount(1, 1), counts["monsters"]);
        Assert.True(status["monsters"]);
        Assert.Equal(new CleanCount(0, 3), counts["raids"]);
        Assert.False(status["raids"]);
    }
}
