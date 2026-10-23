using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
    private readonly CapturingLogger _log = new();
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

        this._job = new RuleRepairJob(provider.GetRequiredService<IServiceScopeFactory>(), this._log);

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

    /// <summary>
    /// A field the server's own schema declares travels back on an edit, so a rule carrying one is editable.
    /// The scan used to judge rules without that list, and reported every personal shiny rule as uneditable.
    /// </summary>
    [Fact]
    public async Task ScanAndApplyJudgeRulesAgainstWhatTheServerDeclares()
    {
        this._tracking
            .Setup(t => t.GetDeclaredRulePropertiesAsync("pokemon"))
            .ReturnsAsync(new HashSet<string> { "personal_shiny", "personal_background" });
        this._tracking
            .Setup(t => t.GetDeclaredRulePropertiesAsync("invasion"))
            .ReturnsAsync(new HashSet<string> { "future_flag" });
        this.Humans("alice");
        this.Rules("alice",
            Rule("pokemon", 1, 30, """{"pokemon_id":25,"personal_shiny":true}"""),
            Rule("pokemon", 1, 31, """{"pokemon_id":26,"undeclared_flag":true}"""),
            Rule("invasion", 1, 32, """{"grunt_type":"metal","future_flag":true}"""));

        var status = await this.ScanAsync();

        Assert.Equal(32, Assert.Single(status.Repairable).Uid);
        Assert.Equal(31, Assert.Single(status.CannotEdit).Uid);

        var applied = await this.ApplyAsync();

        Assert.Equal(1, applied.Applied!.Repaired);
        var write = Assert.Single(this._writes);
        Assert.True(write.Row.GetProperty("future_flag").GetBoolean());
    }

    /// <summary>
    /// The rows named a user by Discord id alone. The human list the scan already reads carries names, so
    /// the status maps each id in its lists to one; nobody outside the lists, and nobody without a name.
    /// </summary>
    [Fact]
    public async Task ScanNamesTheHumansItLists()
    {
        this._humans
            .Setup(h => h.ListHumansAsync(null, null))
            .ReturnsAsync([
                new Human { Id = "alice", Name = "Alice" },
                new Human { Id = "bob", Name = "" },
                new Human { Id = "carol", Name = "Carol" },
            ]);
        this.Rules("alice", Rule("invasion", 1, 10, """{"grunt_type":"metal","gender":"any"}"""));
        this.Rules("bob", Rule("egg", 0, 20, """{"level":0}"""));
        this.Rules("carol");

        var status = await this.ScanAsync();

        Assert.Equal(new Dictionary<string, string> { ["alice"] = "Alice" }, status.HumanNames);
        Assert.Null(status.Summary().HumanNames);
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
            .ThrowsAsync(new PoracleAmbiguousAnswerException("ambiguous"));

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
            .ThrowsAsync(new PoracleAmbiguousAnswerException("PoracleNG answered nothing explicit"));

        var status = await this.ApplyAsync();

        Assert.NotNull(status.Applied);
        Assert.Equal(1, status.Applied.Repaired);
        var conflict = Assert.Single(status.Applied.Conflicts);
        Assert.Equal((10, "invasion", 1), (conflict.Uid, conflict.Type, conflict.ProfileNo));
        Assert.Contains("duplicate", conflict.Reason, StringComparison.Ordinal);
        Assert.Equal(1, status.Applied.Gone);
        var failed = Assert.Single(status.Applied.Failed);
        Assert.Equal(12, failed.Uid);
        Assert.Contains("nothing explicit", failed.Reason);
        Assert.Equal([13], this._writes.Select(w => w.Uid));
    }

    [Fact]
    public async Task ScanFailsOnAnUnexpectedErrorRatherThanSkippingTheHuman()
    {
        this.Humans("alice");
        this._tracking.Setup(t => t.GetStoredRulesAsync("alice")).ThrowsAsync(new InvalidOperationException("a bug"));

        var status = await this.ScanAsync();

        Assert.Equal(RuleRepairPhase.Failed, status.Phase);
        Assert.Contains("a bug", status.Error, StringComparison.Ordinal);
        Assert.Contains("while processing user alice", status.Error, StringComparison.Ordinal);
        Assert.Empty(status.Skipped);
    }

    [Fact]
    public async Task AnUnexpectedErrorOnAWebhookNamesItWithoutItsUrl()
    {
        const string webhook = "https://discord.com/api/webhooks/123/secret-token";
        this.Humans(webhook);
        this._tracking.Setup(t => t.GetStoredRulesAsync(webhook)).ThrowsAsync(new InvalidOperationException("a bug"));

        var status = await this.ScanAsync();

        Assert.Equal(RuleRepairPhase.Failed, status.Phase);
        Assert.Contains("while processing a webhook", status.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-token", status.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("discord.com", status.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyFailedByAnUnexpectedErrorNamesTheHuman()
    {
        this.Humans("alice");
        this.Rules("alice", Rule("invasion", 1, 10, """{"grunt_type":"metal"}"""));
        await this.ScanAsync();
        this._tracking
            .Setup(t => t.ReplaceStoredRuleAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<JsonElement>(), It.IsAny<bool>()))
            .ThrowsAsync(new InvalidOperationException("a bug"));

        var status = await this.ApplyAsync();

        Assert.Equal(RuleRepairPhase.Failed, status.Phase);
        Assert.Contains("a bug (while processing user alice)", status.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("123456789", "user 123456789")]
    [InlineData("http://host:4000/hook", "a webhook")]
    [InlineData("https://discord.com/api/webhooks/1/x", "a webhook")]
    public void HumanLabelNeverPrintsAWebhookUrl(string id, string expected) =>
        Assert.Equal(expected, RuleRepairJob.HumanLabel(id));

    [Fact]
    public async Task ScanFailsWhenNoHumanCouldBeReached()
    {
        this.Humans("alice", "bob");
        this._tracking.Setup(t => t.GetStoredRulesAsync(It.IsAny<string>())).ThrowsAsync(new HttpRequestException("connection refused"));

        var status = await this.ScanAsync();

        Assert.Equal(RuleRepairPhase.Failed, status.Phase);
        Assert.Contains("could not be reached", status.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ScanStopsAfterFiveUnreachableReadsInARow()
    {
        var ids = Enumerable.Range(0, 30).Select(i => $"h{i}").ToArray();
        this.Humans(ids);
        var reads = 0;
        this._tracking
            .Setup(t => t.GetStoredRulesAsync(It.IsAny<string>()))
            .Returns(() =>
            {
                Interlocked.Increment(ref reads);
                return Task.FromException<IReadOnlyList<StoredRule>>(new TaskCanceledException("timed out"));
            });

        var status = await this.ScanAsync();

        Assert.Equal(RuleRepairPhase.Failed, status.Phase);
        Assert.Contains("could not be reached", status.Error, StringComparison.Ordinal);

        // Four humans in flight may each have started a read when the fifth failure lands.
        Assert.InRange(reads, 5, 5 + RuleRepairJob.MaxHumansInFlight);
    }

    [Fact]
    public async Task ScanSkipsAnUnreachableHumanAmongReachableOnes()
    {
        this.Humans("alice", "bob", "carol");
        this._tracking.Setup(t => t.GetStoredRulesAsync("bob")).ThrowsAsync(new HttpRequestException("connection reset"));
        this.Rules("alice", Rule("invasion", 1, 10, """{"grunt_type":"metal"}"""));
        this.Rules("carol");

        var status = await this.ScanAsync();

        Assert.Equal(RuleRepairPhase.Scanned, status.Phase);
        Assert.Equal("bob", Assert.Single(status.Skipped).HumanId);
        Assert.Single(status.Repairable);
    }

    [Fact]
    public async Task ApplyWritesTheRuleAsItIsNowNotAsScanned()
    {
        this.Humans("alice");
        this.Rules("alice", Rule("invasion", 1, 10, """{"grunt_type":"metal","distance":100,"template":"old"}"""));
        await this.ScanAsync();

        // Somebody edited the rule between the scan and the apply.
        this.Rules("alice", Rule("invasion", 1, 10, """{"grunt_type":"metal","distance":900,"template":"new"}"""));

        var status = await this.ApplyAsync();

        Assert.Equal(1, status.Applied!.Repaired);
        var write = Assert.Single(this._writes);
        Assert.Equal(900, write.Row.GetProperty("distance").GetInt32());
        Assert.Equal("new", write.Row.GetProperty("template").GetString());
        Assert.Equal("steel", write.Row.GetProperty("grunt_type").GetString());
    }

    [Fact]
    public async Task ApplyLeavesARuleRepairedByHandSinceTheScanAlone()
    {
        this.Humans("alice");
        this.Rules("alice", Rule("invasion", 1, 10, """{"grunt_type":"metal"}"""));
        await this.ScanAsync();

        this.Rules("alice", Rule("invasion", 1, 10, """{"grunt_type":"steel"}"""));

        var status = await this.ApplyAsync();

        Assert.Equal(0, status.Applied!.Repaired);
        Assert.Equal(1, status.Applied.AlreadyFine);
        this._tracking.Verify(t => t.ReplaceStoredRuleAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<JsonElement>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task ApplyCountsARuleDeletedSinceTheScanAsGone()
    {
        this.Humans("alice");
        this.Rules("alice",
            Rule("invasion", 1, 10, """{"grunt_type":"metal"}"""),
            Rule("invasion", 2, 11, """{"grunt_type":"metal"}"""));
        await this.ScanAsync();

        // uid 11 is gone from profile 2; a rule with the same uid on another profile is not the same rule.
        this.Rules("alice",
            Rule("invasion", 1, 10, """{"grunt_type":"metal"}"""),
            Rule("invasion", 3, 11, """{"grunt_type":"metal"}"""));

        var status = await this.ApplyAsync();

        Assert.Equal(1, status.Applied!.Repaired);
        Assert.Equal(1, status.Applied.Gone);
        Assert.Equal([10], this._writes.Select(w => w.Uid));
    }

    [Fact]
    public async Task ApplyCountsEveryRuleOfAnAccountDeletedSinceTheScanAsGone()
    {
        this.Humans("alice");
        this.Rules("alice",
            Rule("invasion", 1, 10, """{"grunt_type":"metal"}"""),
            Rule("invasion", 1, 11, """{"grunt_type":"metal"}"""));
        await this.ScanAsync();
        this._tracking.Setup(t => t.GetStoredRulesAsync("alice")).ThrowsAsync(new AccountGoneException());

        var status = await this.ApplyAsync();

        Assert.Equal(RuleRepairPhase.Applied, status.Phase);
        Assert.Equal(2, status.Applied!.Gone);
        Assert.Empty(this._writes);
    }

    [Fact]
    public async Task ApplyFailsTheRunWhenPoracleNGIsTooOld()
    {
        this.Humans("alice");
        this.Rules("alice",
            Rule("invasion", 1, 10, """{"grunt_type":"metal"}"""),
            Rule("invasion", 1, 11, """{"grunt_type":"metal"}"""));
        await this.ScanAsync();
        this._tracking
            .Setup(t => t.ReplaceStoredRuleAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<JsonElement>(), It.IsAny<bool>()))
            .ThrowsAsync(new PoracleUnsupportedException("Rule repair", "PoracleNG 5.4.0"));

        var status = await this.ApplyAsync();

        Assert.Equal(RuleRepairPhase.Failed, status.Phase);
        Assert.Contains("5.4.0", status.Error, StringComparison.Ordinal);
        Assert.Null(status.Applied);
        this._tracking.Verify(t => t.ReplaceStoredRuleAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<JsonElement>(), It.IsAny<bool>()), Times.Once);
    }

    [Fact]
    public async Task ApplyStopsAfterFiveUnreachableRequestsInARow()
    {
        this.Humans("alice");
        this.Rules("alice", Enumerable.Range(10, 10).Select(uid => Rule("invasion", 1, uid, """{"grunt_type":"metal"}""")).ToArray());
        await this.ScanAsync();
        this._tracking
            .Setup(t => t.ReplaceStoredRuleAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<JsonElement>(), It.IsAny<bool>()))
            .ThrowsAsync(new HttpRequestException("connection refused"));

        var status = await this.ApplyAsync();

        Assert.Equal(RuleRepairPhase.Failed, status.Phase);
        Assert.Contains("could not be reached", status.Error, StringComparison.Ordinal);
        this._tracking.Verify(t => t.ReplaceStoredRuleAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<JsonElement>(), It.IsAny<bool>()), Times.Exactly(5));
    }

    [Fact]
    public async Task ApplyKeepsGoingPastFewerThanFiveUnreachableRequests()
    {
        this.Humans("alice");
        this.Rules("alice", Enumerable.Range(10, 6).Select(uid => Rule("invasion", 1, uid, """{"grunt_type":"metal"}""")).ToArray());
        await this.ScanAsync();
        foreach (var uid in new[] { 10, 11, 12, 13 })
        {
            this._tracking
                .Setup(t => t.ReplaceStoredRuleAsync(It.IsAny<string>(), It.IsAny<string>(), uid, It.IsAny<int>(), It.IsAny<JsonElement>(), true))
                .ThrowsAsync(new HttpRequestException("connection reset"));
        }

        var status = await this.ApplyAsync();

        Assert.Equal(RuleRepairPhase.Applied, status.Phase);
        Assert.Equal(2, status.Applied!.Repaired);
        Assert.Equal([10, 11, 12, 13], status.Applied.Failed.Select(f => f.Uid));
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
    public async Task ScanFailsWhenEveryHumanIsSkippedWhateverTheReason()
    {
        // A reverse proxy answering 500 for everybody is a status code, not "no response", yet nothing was read.
        this.Humans("alice", "bob", "carol", "dave");
        this._tracking.Setup(t => t.GetStoredRulesAsync(It.IsAny<string>()))
            .ThrowsAsync(new HttpRequestException("Response status code does not indicate success: 500 (Internal Server Error).", null, HttpStatusCode.InternalServerError));
        this._tracking.Setup(t => t.GetStoredRulesAsync("dave")).ThrowsAsync(new AccountGoneException());

        var status = await this.ScanAsync();

        Assert.Equal(RuleRepairPhase.Failed, status.Phase);
        Assert.Contains("PoracleNG answered 500", status.Error, StringComparison.Ordinal);
        Assert.Contains("3 of 4", status.Error, StringComparison.Ordinal);
        Assert.False(this._job.TryStartApply());
    }

    [Fact]
    public async Task ScanWithOneHumanReadIsNotAFailureEvenIfEveryOtherIsSkipped()
    {
        this.Humans("alice", "bob", "carol");
        this._tracking.Setup(t => t.GetStoredRulesAsync(It.IsAny<string>()))
            .ThrowsAsync(new HttpRequestException("boom", null, HttpStatusCode.InternalServerError));
        this.Rules("carol");

        var status = await this.ScanAsync();

        Assert.Equal(RuleRepairPhase.Scanned, status.Phase);
        Assert.Equal(2, status.Skipped.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task ScanStopsAfterFiveGatewayErrorsInARow(HttpStatusCode gateway)
    {
        this.Humans(Enumerable.Range(0, 30).Select(i => $"h{i}").ToArray());
        var reads = 0;
        this._tracking
            .Setup(t => t.GetStoredRulesAsync(It.IsAny<string>()))
            .Returns(() =>
            {
                Interlocked.Increment(ref reads);
                return Task.FromException<IReadOnlyList<StoredRule>>(new HttpRequestException("bad gateway", null, gateway));
            });

        var status = await this.ScanAsync();

        Assert.Equal(RuleRepairPhase.Failed, status.Phase);
        Assert.Contains("could not be reached", status.Error, StringComparison.Ordinal);
        Assert.InRange(reads, 5, 5 + RuleRepairJob.MaxHumansInFlight);
    }

    [Fact]
    public async Task ScanDoesNotStopOnInternalServerErrorsInARow()
    {
        // A 500 is about the human PoracleNG was asked for, so it is skipped, not counted as PoracleNG gone.
        this.Humans([.. Enumerable.Range(0, 10).Select(i => $"h{i}"), "zed"]);
        this._tracking.Setup(t => t.GetStoredRulesAsync(It.Is<string>(id => id.StartsWith('h'))))
            .ThrowsAsync(new HttpRequestException("boom", null, HttpStatusCode.InternalServerError));
        this.Rules("zed", Rule("invasion", 1, 10, """{"grunt_type":"metal"}"""));

        var status = await this.ScanAsync();

        Assert.Equal(RuleRepairPhase.Scanned, status.Phase);
        Assert.Equal(10, status.Skipped.Count);
        Assert.Single(status.Repairable);
    }

    [Fact]
    public async Task ScanListsAnAnyTierRuleWhoseRepairWouldConflict()
    {
        this.Humans("alice");
        this.Rules("alice",
            Rule("raid", 1, 30, """{"pokemon_id":null,"level":null}"""),
            Rule("raid", 1, 31, """{"pokemon_id":null,"level":null}"""));
        this.DryRunAnswers(30, StoredRuleReplaceOutcome.Conflict);
        this.DryRunAnswers(31, StoredRuleReplaceOutcome.Updated);

        var status = await this.ScanAsync();

        Assert.Equal(31, Assert.Single(status.Repairable).Uid);
        var conflict = Assert.Single(status.CannotEdit);
        Assert.Equal((30, RuleRepairKind.CannotEdit), (conflict.Uid, conflict.Kind));
        Assert.Contains("duplicate", conflict.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyLogsTheStoredRuleJustBeforeEachRealWrite()
    {
        this.Humans("alice");
        this.Rules("alice", Rule("invasion", 2, 10, """{"grunt_type":"metal","distance":250}"""));
        await this.ScanAsync();
        this._log.Entries.Clear();

        await this.ApplyAsync();

        var audit = Assert.Single(this._log.Entries, e => e.Message.Contains("Rule repair writing", StringComparison.Ordinal));
        Assert.Equal(LogLevel.Information, audit.Level);
        Assert.Contains("human alice", audit.Message, StringComparison.Ordinal);
        Assert.Contains("invasion", audit.Message, StringComparison.Ordinal);
        Assert.Contains("profile 2", audit.Message, StringComparison.Ordinal);
        Assert.Contains("uid 10", audit.Message, StringComparison.Ordinal);
        Assert.Contains("MetalGrunt", audit.Message, StringComparison.Ordinal);
        Assert.Contains("""{"grunt_type":"metal","distance":250}""", audit.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyLogsNothingForARuleItDoesNotWrite()
    {
        this.Humans("alice");
        this.Rules("alice", Rule("invasion", 1, 10, """{"grunt_type":"metal"}"""));
        await this.ScanAsync();
        this.DryRunAnswers(10, StoredRuleReplaceOutcome.Unchanged);

        var status = await this.ApplyAsync();

        Assert.Equal(1, status.Applied!.AlreadyFine);
        Assert.DoesNotContain(this._log.Entries, e => e.Message.Contains("Rule repair writing", StringComparison.Ordinal));
    }

    [Fact]
    public void SummaryKeepsTheCountsAndDropsTheLists()
    {
        var item = new RuleRepairItem("alice", "invasion", 1, 10, RuleRepairKind.MetalGrunt, "grunt_type metal", "grunt_type steel", null);
        var full = RuleRepairStatus.Idle with
        {
            Phase = RuleRepairPhase.Scanned,
            Repairable = [item, item with { Uid = 11 }],
            CannotEdit = [item with { Kind = RuleRepairKind.CannotEdit }],
            Skipped = [new RuleRepairSkip("bob", "gone"), new RuleRepairSkip("carol", "gone"), new RuleRepairSkip("dave", "gone")],
        };

        var summary = full.Summary();

        Assert.Equal((2, 1, 3), (full.RepairableCount, full.CannotEditCount, full.SkippedCount));
        Assert.Equal((2, 1, 3), (summary.RepairableCount, summary.CannotEditCount, summary.SkippedCount));
        Assert.Empty(summary.Repairable);
        Assert.Empty(summary.CannotEdit);
        Assert.Empty(summary.Skipped);
        Assert.Equal(RuleRepairPhase.Scanned, summary.Phase);

        var json = JsonSerializer.Serialize(summary, WebJson);
        Assert.Contains("\"repairableCount\":2", json, StringComparison.Ordinal);
        Assert.Contains("\"cannotEditCount\":1", json, StringComparison.Ordinal);
        Assert.Contains("\"skippedCount\":3", json, StringComparison.Ordinal);
        Assert.Contains("\"repairable\":[]", json, StringComparison.Ordinal);
    }

    [Fact]
    public void FullStatusCarriesTheCountsToo()
    {
        var full = RuleRepairStatus.Idle with { Skipped = [new RuleRepairSkip("bob", "gone")] };

        var json = JsonSerializer.Serialize(full, WebJson);

        Assert.Contains("\"skippedCount\":1", json, StringComparison.Ordinal);
        Assert.Contains("\"repairableCount\":0", json, StringComparison.Ordinal);
    }

    private sealed class CapturingLogger : ILogger<RuleRepairJob>
    {
        public ConcurrentQueue<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            this.Entries.Enqueue((logLevel, formatter(state, exception)));
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
