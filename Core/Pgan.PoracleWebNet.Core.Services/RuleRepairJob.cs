using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

/// <summary>
/// Finds the stored rules PoracleNG 5.4.0's <c>/api/v2</c> reads differently and repairs them through v2,
/// one scan or apply at a time.
/// </summary>
/// <remarks>
/// A singleton. Each run is a background task, and each human it touches gets a DI scope of its own, so a
/// run never shares the request's scope and four humans in flight never share a proxy instance. State is
/// an immutable <see cref="RuleRepairStatus"/> swapped under a lock, so <see cref="GetStatus"/> never sees
/// half an update.
/// </remarks>
public sealed partial class RuleRepairJob(IServiceScopeFactory scopes, ILogger<RuleRepairJob> logger) : IRuleRepairJob
{
    /// <summary>How many humans are read or repaired at once.</summary>
    internal const int MaxHumansInFlight = 4;

    /// <summary>What an any-tier raid or max battle is stored as when its dry run answers <c>updated</c>.</summary>
    private const string StoredAnyTier = "pokemon 9000, level 9000";

    private readonly IServiceScopeFactory _scopes = scopes;
    private readonly ILogger<RuleRepairJob> _logger = logger;
    private readonly Lock _gate = new();

    private RuleRepairStatus _status = RuleRepairStatus.Idle;

    /// <summary>The last scan's repairable items with the row each one PUTs. Swapped whole, never mutated.</summary>
    private IReadOnlyList<Repair> _repairs = [];

    /// <summary>The running (or last) job. Tests await it rather than sleeping.</summary>
    internal Task Completion { get; private set; } = Task.CompletedTask;

    public RuleRepairStatus GetStatus()
    {
        lock (this._gate)
        {
            return this._status;
        }
    }

    public bool TryStartScan()
    {
        lock (this._gate)
        {
            if (IsBusy(this._status.Phase))
            {
                return false;
            }

            this._status = RuleRepairStatus.Idle with { Phase = RuleRepairPhase.Scanning, StartedAt = DateTimeOffset.UtcNow };
            this._repairs = [];
            this.Completion = Task.Run(this.RunScanAsync);
            return true;
        }
    }

    public bool TryStartApply()
    {
        lock (this._gate)
        {
            if (IsBusy(this._status.Phase) || this._status.Phase is not (RuleRepairPhase.Scanned or RuleRepairPhase.Applied))
            {
                return false;
            }

            var humans = this._repairs.Select(r => r.Item.HumanId).Distinct(StringComparer.Ordinal).Count();
            this._status = this._status with
            {
                Phase = RuleRepairPhase.Applying,
                HumansTotal = humans,
                HumansDone = 0,
                StartedAt = DateTimeOffset.UtcNow,
                FinishedAt = null,
                Applied = null,
                Error = null,
            };
            this.Completion = Task.Run(this.RunApplyAsync);
            return true;
        }
    }

    private static bool IsBusy(RuleRepairPhase phase) => phase is RuleRepairPhase.Scanning or RuleRepairPhase.Applying;

    private async Task RunScanAsync()
    {
        try
        {
            IReadOnlyList<Human> humans;
            using (var scope = this._scopes.CreateScope())
            {
                humans = await scope.ServiceProvider.GetRequiredService<IPoracleHumanProxy>().ListHumansAsync();
            }

            var ids = humans
                .Select(h => h.Id)
                .Where(id => !string.IsNullOrEmpty(id))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            this.Update(s => s with { HumansTotal = ids.Count });

            var results = new HumanScan[ids.Count];
            await Parallel.ForEachAsync(
                Enumerable.Range(0, ids.Count),
                new ParallelOptions { MaxDegreeOfParallelism = MaxHumansInFlight },
                async (index, _) =>
                {
                    results[index] = await this.ScanHumanAsync(ids[index]);
                    this.Update(s => s with { HumansDone = s.HumansDone + 1 });
                });

            var repairs = results.SelectMany(r => r.Repairs).OrderBy(r => r.Item, ItemOrder.Instance).ToList();
            var cannotEdit = results.SelectMany(r => r.CannotEdit).OrderBy(i => i, ItemOrder.Instance).ToList();
            var skipped = results.Where(r => r.Skip is not null).Select(r => r.Skip!).OrderBy(s => s.HumanId, StringComparer.Ordinal).ToList();

            lock (this._gate)
            {
                this._repairs = repairs;
                this._status = this._status with
                {
                    Phase = RuleRepairPhase.Scanned,
                    FinishedAt = DateTimeOffset.UtcNow,
                    Repairable = repairs.Select(r => r.Item).ToList(),
                    CannotEdit = cannotEdit,
                    Skipped = skipped,
                };
            }

            LogScanned(this._logger, ids.Count, repairs.Count, cannotEdit.Count, skipped.Count);
        }
        catch (Exception ex)
        {
            this.Fail(ex, "scan");
        }
    }

    /// <summary>
    /// Reads one human's rules and classifies them, confirming any-tier candidates with a dry run. Anything
    /// that goes wrong for this human skips the human as a whole, so a half-read account never reports a
    /// partial list as if it were complete. A server too old for the read fails the run instead.
    /// </summary>
    private async Task<HumanScan> ScanHumanAsync(string humanId)
    {
        var repairs = new List<Repair>();
        var cannotEdit = new List<RuleRepairItem>();

        try
        {
            using var scope = this._scopes.CreateScope();
            var tracking = scope.ServiceProvider.GetRequiredService<IPoracleTrackingProxy>();

            foreach (var rule in await tracking.GetStoredRulesAsync(humanId))
            {
                var candidate = RuleRepairClassifier.Classify(rule, humanId);
                if (candidate is null)
                {
                    continue;
                }

                if (candidate.RepairedV1Row is not { } row)
                {
                    cannotEdit.Add(candidate.Item);
                    continue;
                }

                var item = candidate.Item;
                if (candidate.NeedsDryRunToConfirm)
                {
                    StoredRuleReplaceOutcome outcome;
                    try
                    {
                        outcome = await tracking.ReplaceStoredRuleAsync(item.Type, humanId, item.Uid, item.ProfileNo, row, dryRun: true);
                    }
                    catch (AlarmValidationException ex)
                    {
                        cannotEdit.Add(item with { Kind = RuleRepairKind.CannotEdit, After = null, Reason = ex.Message });
                        continue;
                    }

                    // Only "updated" says the stored rule is 9000/9000; anything else leaves nothing to repair.
                    if (outcome != StoredRuleReplaceOutcome.Updated)
                    {
                        continue;
                    }

                    item = item with { Before = StoredAnyTier };
                }

                repairs.Add(new Repair(item, row));
            }

            return new HumanScan(repairs, cannotEdit, null);
        }
        catch (Exception ex) when (SkipReason(ex) is { } reason)
        {
            LogHumanSkipped(this._logger, humanId, reason);
            return new HumanScan([], [], new RuleRepairSkip(humanId, reason));
        }
    }

    /// <summary>Why one human could not be read, or null when the failure is the whole run's.</summary>
    private static string? SkipReason(Exception ex) => ex switch
    {
        PoracleUnsupportedException => null,
        AccountGoneException => "The account no longer exists.",
        HttpRequestException { StatusCode: { } status } http => $"PoracleNG answered {(int)status}: {http.Message}",
        HttpRequestException http => $"PoracleNG could not be reached: {http.Message}",
        TaskCanceledException => "PoracleNG did not answer in time.",
        JsonException json => $"PoracleNG's answer could not be read: {json.Message}",
        InvalidOperationException op => op.Message,
        _ => null,
    };

    private async Task RunApplyAsync()
    {
        try
        {
            IReadOnlyList<Repair> repairs;
            lock (this._gate)
            {
                repairs = this._repairs;
            }

            var byHuman = repairs.GroupBy(r => r.Item.HumanId, StringComparer.Ordinal).ToList();
            var tallies = new ApplyTally[byHuman.Count];

            await Parallel.ForEachAsync(
                Enumerable.Range(0, byHuman.Count),
                new ParallelOptions { MaxDegreeOfParallelism = MaxHumansInFlight },
                async (index, _) =>
                {
                    tallies[index] = await this.ApplyHumanAsync(byHuman[index].ToList());
                    this.Update(s => s with { HumansDone = s.HumansDone + 1 });
                });

            var result = new RuleRepairApplyResult(
                tallies.Sum(t => t.Repaired),
                tallies.Sum(t => t.AlreadyFine),
                tallies.Sum(t => t.Conflicts),
                tallies.Sum(t => t.Gone),
                tallies.SelectMany(t => t.Failed).OrderBy(i => i, ItemOrder.Instance).ToList());

            this.Update(s => s with { Phase = RuleRepairPhase.Applied, FinishedAt = DateTimeOffset.UtcNow, Applied = result });

            LogApplied(this._logger, result.Repaired, result.AlreadyFine, result.Conflicts, result.Gone, result.Failed.Count);
        }
        catch (Exception ex)
        {
            this.Fail(ex, "apply");
        }
    }

    /// <summary>
    /// Repairs one human's items in turn, each judged by its own dry run so a rule changed since the scan is
    /// judged on what is stored now. A conflict is left as it is.
    /// </summary>
    private async Task<ApplyTally> ApplyHumanAsync(List<Repair> repairs)
    {
        var tally = new ApplyTally();
        using var scope = this._scopes.CreateScope();
        var tracking = scope.ServiceProvider.GetRequiredService<IPoracleTrackingProxy>();

        foreach (var (item, row) in repairs)
        {
            try
            {
                var outcome = await tracking.ReplaceStoredRuleAsync(item.Type, item.HumanId, item.Uid, item.ProfileNo, row, dryRun: true);
                if (outcome == StoredRuleReplaceOutcome.Updated)
                {
                    // The uid may come back different; nothing in PoracleWeb.NET tracks these rules by uid.
                    outcome = await tracking.ReplaceStoredRuleAsync(item.Type, item.HumanId, item.Uid, item.ProfileNo, row, dryRun: false);
                    if (outcome == StoredRuleReplaceOutcome.Updated)
                    {
                        tally.Repaired++;
                        continue;
                    }
                }

                switch (outcome)
                {
                    case StoredRuleReplaceOutcome.Unchanged:
                        tally.AlreadyFine++;
                        break;
                    case StoredRuleReplaceOutcome.Conflict:
                        tally.Conflicts++;
                        break;
                    default:
                        tally.Gone++;
                        break;
                }
            }
            catch (AccountGoneException)
            {
                tally.Gone++;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                LogRepairFailed(this._logger, item.Type, item.Uid, item.HumanId, ex.Message);
                tally.Failed.Add(item with { Reason = ex.Message });
            }
        }

        return tally;
    }

    private void Update(Func<RuleRepairStatus, RuleRepairStatus> change)
    {
        lock (this._gate)
        {
            this._status = change(this._status);
        }
    }

    private void Fail(Exception ex, string run)
    {
        LogRunFailed(this._logger, run, ex.Message);
        this.Update(s => s with { Phase = RuleRepairPhase.Failed, FinishedAt = DateTimeOffset.UtcNow, Error = ex.Message });
    }

    private sealed record Repair(RuleRepairItem Item, JsonElement Row);

    private sealed record HumanScan(IReadOnlyList<Repair> Repairs, IReadOnlyList<RuleRepairItem> CannotEdit, RuleRepairSkip? Skip);

    private sealed class ApplyTally
    {
        public int Repaired { get; set; }
        public int AlreadyFine { get; set; }
        public int Conflicts { get; set; }
        public int Gone { get; set; }
        public List<RuleRepairItem> Failed { get; } = [];
    }

    /// <summary>Human, then type, then uid, so the report reads the same on every run.</summary>
    private sealed class ItemOrder : IComparer<RuleRepairItem>
    {
        public static ItemOrder Instance { get; } = new();

        public int Compare(RuleRepairItem? x, RuleRepairItem? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x is null)
            {
                return -1;
            }

            if (y is null)
            {
                return 1;
            }

            var byHuman = string.CompareOrdinal(x.HumanId, y.HumanId);
            if (byHuman != 0)
            {
                return byHuman;
            }

            var byType = string.CompareOrdinal(x.Type, y.Type);
            return byType != 0 ? byType : x.Uid.CompareTo(y.Uid);
        }
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Rule repair scan read {Humans} humans: {Repairable} repairable, {CannotEdit} cannot be edited, {Skipped} skipped.")]
    private static partial void LogScanned(ILogger logger, int humans, int repairable, int cannotEdit, int skipped);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Rule repair applied: {Repaired} repaired, {AlreadyFine} already fine, {Conflicts} conflicts, {Gone} gone, {Failed} failed.")]
    private static partial void LogApplied(ILogger logger, int repaired, int alreadyFine, int conflicts, int gone, int failed);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Rule repair scan skipped human {HumanId}: {Reason}")]
    private static partial void LogHumanSkipped(ILogger logger, string humanId, string reason);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Rule repair could not repair {Type} rule uid={Uid} of human {HumanId}: {Reason}")]
    private static partial void LogRepairFailed(ILogger logger, string type, int uid, string humanId, string reason);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Rule repair {Run} failed: {Reason}")]
    private static partial void LogRunFailed(ILogger logger, string run, string reason);
}
