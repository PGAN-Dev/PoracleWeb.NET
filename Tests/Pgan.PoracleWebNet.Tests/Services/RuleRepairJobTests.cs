using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

public class RuleRepairJobTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private readonly Mock<IPoracleTrackingProxy> _tracking = new();
    private readonly Mock<IPoracleHumanProxy> _humans = new();
    private readonly RuleRepairJob _job;

    /// <summary>Real (non dry-run) PUTs, in the order sent.</summary>
    private readonly ConcurrentQueue<(string Type, string UserId, int Uid, int ProfileNo, JsonElement Row)> _writes = new();

    /// <summary>uids whose stored rule already matches what a repair would send.</summary>
    private readonly ConcurrentDictionary<int, bool> _fine = new();

    public RuleRepairJobTests()
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => this._tracking.Object);
        services.AddScoped(_ => this._humans.Object);
        var provider = services.BuildServiceProvider();

        this._job = new RuleRepairJob(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<RuleRepairJob>.Instance);

        // By default a dry run says the repair would change something until a real PUT has made it so.
        this._tracking
            .Setup(t => t.ReplaceStoredRuleAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<JsonElement>(), It.IsAny<bool>()))
            .ReturnsAsync((string type, string userId, int uid, int profileNo, JsonElement row, bool dryRun) =>
            {
                if (this._fine.ContainsKey(uid))
                {
                    return StoredRuleReplaceOutcome.Unchanged;
                }

                if (!dryRun)
                {
                    this._writes.Enqueue((type, userId, uid, profileNo, row));
                    this._fine[uid] = true;
                }

                return StoredRuleReplaceOutcome.Updated;
            });
    }

    private static StoredRule Rule(string type, int profileNo, int uid, string json) =>
        new(type, profileNo, uid, JsonDocument.Parse(json).RootElement.Clone());

    private void Humans(params string[] ids) =>
        this._humans
            .Setup(h => h.ListHumansAsync(null, null))
            .ReturnsAsync(ids.Select(id => new Human { Id = id }).ToList());

    private void Rules(string humanId, params StoredRule[] rules) =>
        this._tracking.Setup(t => t.GetStoredRulesAsync(humanId)).ReturnsAsync(rules);

    private async Task<RuleRepairStatus> ScanAsync()
    {
        Assert.True(this._job.TryStartScan());
        await this._job.Completion;
        return this._job.GetStatus();
    }

    private async Task<RuleRepairStatus> ApplyAsync()
    {
        Assert.True(this._job.TryStartApply());
        await this._job.Completion;
        return this._job.GetStatus();
    }

    private void DryRunAnswers(int uid, StoredRuleReplaceOutcome outcome) =>
        this._tracking
            .Setup(t => t.ReplaceStoredRuleAsync(It.IsAny<string>(), It.IsAny<string>(), uid, It.IsAny<int>(), It.IsAny<JsonElement>(), true))
            .ReturnsAsync(outcome);

    [Fact]
    public async Task ScanFindsRepairableAndCannotEdit()
    {
        this.Humans("alice", "bob");
        this.Rules("alice",
            Rule("invasion", 1, 10, """{"grunt_type":"metal","gender":"any"}"""),
            Rule("raid", 2, 11, """{"pokemon_id":0,"level":5}"""),
            Rule("raid", 1, 12, """{"pokemon_id":150,"level":5}"""));
        this.Rules("bob",
            Rule("egg", 0, 20, """{"level":0}"""),
            Rule("incident", 0, 21, """{"display_type":"kecleon"}"""));

        var status = await this.ScanAsync();

        Assert.Equal(RuleRepairPhase.Scanned, status.Phase);
        Assert.Equal(2, status.HumansTotal);
        Assert.Equal(2, status.HumansDone);
        Assert.NotNull(status.StartedAt);
        Assert.NotNull(status.FinishedAt);
        Assert.Empty(status.Skipped);
        Assert.Null(status.Error);

        Assert.Collection(status.Repairable,
            i =>
            {
                Assert.Equal(("alice", "invasion", 1, 10, RuleRepairKind.MetalGrunt), (i.HumanId, i.Type, i.ProfileNo, i.Uid, i.Kind));
            },
            i =>
            {
                Assert.Equal(("alice", "raid", 2, 11, RuleRepairKind.RaidPokemonZero), (i.HumanId, i.Type, i.ProfileNo, i.Uid, i.Kind));
            });
        var cannot = Assert.Single(status.CannotEdit);
        Assert.Equal(("bob", "egg", 20), (cannot.HumanId, cannot.Type, cannot.Uid));

        // The scan never writes, and only any-tier candidates need a dry run.
        Assert.Empty(this._writes);
        this._tracking.Verify(t => t.ReplaceStoredRuleAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<JsonElement>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task ScanWithNothingToRepairIsAnEmptyScan()
    {
        this.Humans("alice");
        this.Rules("alice", Rule("raid", 1, 12, """{"pokemon_id":150,"level":5}"""));

        var status = await this.ScanAsync();

        Assert.Equal(RuleRepairPhase.Scanned, status.Phase);
        Assert.Empty(status.Repairable);
        Assert.Empty(status.CannotEdit);
    }

    [Fact]
    public async Task ScanConfirmsAnyTierRaidsWithADryRun()
    {
        this.Humans("alice");
        this.Rules("alice",
            Rule("raid", 1, 30, """{"pokemon_id":null,"level":null}"""),
            Rule("raid", 1, 31, """{"pokemon_id":null,"level":null}"""),
            Rule("maxbattle", 3, 32, """{"pokemon_id":null,"level":null}"""));
        this.DryRunAnswers(30, StoredRuleReplaceOutcome.Updated);
        this.DryRunAnswers(31, StoredRuleReplaceOutcome.Unchanged);
        this.DryRunAnswers(32, StoredRuleReplaceOutcome.Updated);

        var status = await this.ScanAsync();

        Assert.Equal([30, 32], status.Repairable.Select(i => i.Uid).Order());
        Assert.All(status.Repairable, i => Assert.Equal("any boss, any tier", i.After));
        Assert.All(status.Repairable, i => Assert.Equal("pokemon 9000, level 9000", i.Before));
        this._tracking.Verify(t => t.ReplaceStoredRuleAsync("maxbattle", "alice", 32, 3, It.IsAny<JsonElement>(), true), Times.Once);
        this._tracking.Verify(t => t.ReplaceStoredRuleAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<JsonElement>(), false), Times.Never);
    }

    [Fact]
    public async Task ScanSkipsAHumanWhoseReadFails()
    {
        this.Humans("gone", "broken", "refused", "alice");
        this._tracking.Setup(t => t.GetStoredRulesAsync("gone")).ThrowsAsync(new AccountGoneException());
        this._tracking.Setup(t => t.GetStoredRulesAsync("broken")).ThrowsAsync(new HttpRequestException("boom", null, HttpStatusCode.InternalServerError));
        this._tracking.Setup(t => t.GetStoredRulesAsync("refused")).ThrowsAsync(new HttpRequestException("nope", null, HttpStatusCode.Forbidden));
        this.Rules("alice", Rule("invasion", 1, 10, """{"grunt_type":"metal"}"""));

        var status = await this.ScanAsync();

        Assert.Equal(RuleRepairPhase.Scanned, status.Phase);
        Assert.Equal(4, status.HumansDone);
        Assert.Equal(["broken", "gone", "refused"], status.Skipped.Select(s => s.HumanId));
        Assert.All(status.Skipped, s => Assert.False(string.IsNullOrWhiteSpace(s.Reason)));
        Assert.Equal("alice", Assert.Single(status.Repairable).HumanId);
    }

    [Fact]
    public async Task ScanSkipsAHumanWhoseDryRunFailsAndKeepsNoneOfTheirRules()
    {
        this.Humans("alice", "bob");
        this.Rules("alice",
            Rule("invasion", 1, 10, """{"grunt_type":"metal"}"""),
            Rule("raid", 1, 30, """{"pokemon_id":null,"level":null}"""));
        this.Rules("bob", Rule("invasion", 1, 40, """{"grunt_type":"metal"}"""));
        this._tracking
            .Setup(t => t.ReplaceStoredRuleAsync("raid", "alice", 30, 1, It.IsAny<JsonElement>(), true))
            .ThrowsAsync(new InvalidOperationException("ambiguous"));

        var status = await this.ScanAsync();

        Assert.Equal("alice", Assert.Single(status.Skipped).HumanId);
        Assert.Equal("bob", Assert.Single(status.Repairable).HumanId);
    }

    [Fact]
    public async Task ScanThatCannotListHumansFails()
    {
        this._humans.Setup(h => h.ListHumansAsync(null, null)).ThrowsAsync(new PoracleUnsupportedException("Human list", "5.4.0"));

        var status = await this.ScanAsync();

        Assert.Equal(RuleRepairPhase.Failed, status.Phase);
        Assert.False(string.IsNullOrWhiteSpace(status.Error));
        Assert.NotNull(status.FinishedAt);
        Assert.False(this._job.TryStartApply());
    }

    [Fact]
    public async Task SecondScanWhileBusyIsRefused()
    {
        var release = new TaskCompletionSource<IReadOnlyList<Human>>(TaskCreationOptions.RunContinuationsAsynchronously);
        this._humans.Setup(h => h.ListHumansAsync(null, null)).Returns(release.Task);

        Assert.True(this._job.TryStartScan());
        Assert.Equal(RuleRepairPhase.Scanning, this._job.GetStatus().Phase);
        Assert.False(this._job.TryStartScan());
        Assert.False(this._job.TryStartApply());

        release.SetResult([]);
        await this._job.Completion;

        // Once the first has finished, a new scan starts.
        Assert.Equal(RuleRepairPhase.Scanned, this._job.GetStatus().Phase);
        Assert.True(this._job.TryStartScan());
        await this._job.Completion;
    }

    [Fact]
    public void ApplyBeforeAnyScanIsRefused()
    {
        Assert.False(this._job.TryStartApply());
        Assert.Equal(RuleRepairPhase.Idle, this._job.GetStatus().Phase);
    }

    [Fact]
    public async Task ApplyRepairsOnlyWhatTheDryRunConfirms()
    {
        this.Humans("alice", "bob");
        this.Rules("alice",
            Rule("invasion", 1, 10, """{"grunt_type":"metal","gender":"any"}"""),
            Rule("raid", 2, 11, """{"pokemon_id":0,"level":5}"""));
        this.Rules("bob", Rule("invasion", 4, 40, """{"grunt_type":"metal"}"""));
        await this.ScanAsync();

        // uid 11 was fixed by somebody else since the scan.
        this._fine[11] = true;

        var status = await this.ApplyAsync();

        Assert.Equal(RuleRepairPhase.Applied, status.Phase);
        Assert.NotNull(status.Applied);
        Assert.Equal(2, status.Applied.Repaired);
        Assert.Equal(1, status.Applied.AlreadyFine);
        Assert.Empty(status.Applied.Failed);
        Assert.Equal(2, status.HumansTotal);
        Assert.Equal(2, status.HumansDone);

        var writes = this._writes.OrderBy(w => w.Uid).ToList();
        Assert.Equal([10, 40], writes.Select(w => w.Uid));
        Assert.Equal(("invasion", "alice", 1), (writes[0].Type, writes[0].UserId, writes[0].ProfileNo));
        Assert.Equal("steel", writes[0].Row.GetProperty("grunt_type").GetString());
        Assert.Equal(4, writes[1].ProfileNo);

        // Every write was preceded by its own dry run.
        this._tracking.Verify(t => t.ReplaceStoredRuleAsync("invasion", "alice", 10, 1, It.IsAny<JsonElement>(), true), Times.Once);
        this._tracking.Verify(t => t.ReplaceStoredRuleAsync("raid", "alice", 11, 2, It.IsAny<JsonElement>(), false), Times.Never);
    }

    [Fact]
    public async Task ApplyCountsConflictsAndGoneRulesWithoutWriting()
    {
        this.Humans("alice");
        this.Rules("alice",
            Rule("invasion", 1, 10, """{"grunt_type":"metal"}"""),
            Rule("invasion", 1, 11, """{"grunt_type":"metal"}"""),
            Rule("invasion", 1, 12, """{"grunt_type":"metal"}"""),
            Rule("invasion", 1, 13, """{"grunt_type":"metal"}"""));
        await this.ScanAsync();

        this.DryRunAnswers(10, StoredRuleReplaceOutcome.Conflict);
        this.DryRunAnswers(11, StoredRuleReplaceOutcome.NotFound);
        this._tracking
            .Setup(t => t.ReplaceStoredRuleAsync(It.IsAny<string>(), It.IsAny<string>(), 12, It.IsAny<int>(), It.IsAny<JsonElement>(), true))
            .ThrowsAsync(new InvalidOperationException("PoracleNG answered nothing explicit"));

        var status = await this.ApplyAsync();

        Assert.NotNull(status.Applied);
        Assert.Equal(1, status.Applied.Repaired);
        Assert.Equal(1, status.Applied.Conflicts);
        Assert.Equal(1, status.Applied.Gone);
        var failed = Assert.Single(status.Applied.Failed);
        Assert.Equal(12, failed.Uid);
        Assert.Contains("nothing explicit", failed.Reason);
        Assert.Equal([13], this._writes.Select(w => w.Uid));
    }

    [Fact]
    public async Task ApplyTwiceFindsNothingToDo()
    {
        this.Humans("alice");
        this.Rules("alice",
            Rule("invasion", 1, 10, """{"grunt_type":"metal"}"""),
            Rule("raid", 1, 11, """{"pokemon_id":0,"level":5}"""));
        await this.ScanAsync();

        var first = await this.ApplyAsync();
        var second = await this.ApplyAsync();

        Assert.Equal(2, first.Applied!.Repaired);
        Assert.Equal(0, second.Applied!.Repaired);
        Assert.Equal(2, second.Applied.AlreadyFine);
        Assert.Equal(2, this._writes.Count);
    }

    [Fact]
    public async Task NoMoreThanFourHumansAreReadAtOnce()
    {
        var ids = Enumerable.Range(0, 12).Select(i => $"h{i}").ToArray();
        this.Humans(ids);
        var inFlight = 0;
        var peak = 0;
        this._tracking
            .Setup(t => t.GetStoredRulesAsync(It.IsAny<string>()))
            .Returns(async () =>
            {
                var now = Interlocked.Increment(ref inFlight);
                InterlockedMax(ref peak, now);
                await Task.Delay(20);
                Interlocked.Decrement(ref inFlight);
                return (IReadOnlyList<StoredRule>)[];
            });

        var status = await this.ScanAsync();

        Assert.Equal(12, status.HumansDone);
        Assert.InRange(peak, 1, 4);
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        do
        {
            current = target;
            if (value <= current)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref target, value, current) != current);
    }

    [Fact]
    public void StatusSerialisesPhaseAndKindAsNames()
    {
        var status = RuleRepairStatus.Idle with
        {
            Repairable = [new RuleRepairItem("alice", "invasion", 1, 10, RuleRepairKind.MetalGrunt, "grunt_type metal", "grunt_type steel", null)],
        };

        var json = JsonSerializer.Serialize(status, WebJson);

        Assert.Contains("\"phase\":\"Idle\"", json, StringComparison.Ordinal);
        Assert.Contains("\"kind\":\"MetalGrunt\"", json, StringComparison.Ordinal);
    }
}
