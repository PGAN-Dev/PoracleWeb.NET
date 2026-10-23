using System.Net;
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
/// <para>
/// A singleton. Each run is a background task, and each human it touches gets a DI scope of its own, so a
/// run never shares the request's scope and four humans in flight never share a proxy instance. State is
/// an immutable <see cref="RuleRepairStatus"/> swapped under a lock, so <see cref="GetStatus"/> never sees
/// half an update.
/// </para>
/// <para>
/// Apply never writes what the scan saw. It re-reads each human's rules and classifies them again, so an
/// edit made between the scan and the apply is carried by the repair rather than reverted by it.
/// </para>
/// <para>
/// A run stops rather than grinding on when PoracleNG is too old (<see cref="PoracleUnsupportedException"/>)
/// or stops answering (<see cref="MaxUnreachableInARow"/> requests in a row with no response, or a gateway
/// error in front of it). Anything else unexpected fails the run too, so a bug reads as a failure, not as a
/// skipped human. A scan that skipped every human fails as well, whatever the reasons were, because a scan
/// that read nobody has found nothing and must not say "nothing to repair".
/// </para>
/// </remarks>
public sealed partial class RuleRepairJob(IServiceScopeFactory scopes, ILogger<RuleRepairJob> logger) : IRuleRepairJob
{
    /// <summary>How many humans are read or repaired at once.</summary>
    internal const int MaxHumansInFlight = 4;

    /// <summary>Requests in a row that got no response before a run gives up on PoracleNG.</summary>
    internal const int MaxUnreachableInARow = 5;

    /// <summary>Why a rule whose repair would collide with another of the same human's rules was left alone.</summary>
    internal const string DuplicateReason =
        "Repairing it would duplicate another of this user's rules, so it was left as it is. One of the two is probably redundant.";

    /// <summary>What an any-tier raid or max battle is stored as when its dry run answers <c>updated</c>.</summary>
    private const string StoredAnyTier = "pokemon 9000, level 9000";

    private readonly IServiceScopeFactory _scopes = scopes;
    private readonly ILogger<RuleRepairJob> _logger = logger;
    private readonly Lock _gate = new();

    private RuleRepairStatus _status = RuleRepairStatus.Idle;

    /// <summary>Requests in a row, across every human in flight, that got no response. Reset per run.</summary>
    private int _unreachableInARow;

    /// <summary>The failure that tipped <see cref="_unreachableInARow"/> over, once it has.</summary>
    private string? _unreachableReason;

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
            this.ResetUnreachable();
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

            var humans = this._status.Repairable.Select(r => r.HumanId).Distinct(StringComparer.Ordinal).Count();
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
            this.ResetUnreachable();
            this.Completion = Task.Run(this.RunApplyAsync);
            return true;
        }
    }

    private static bool IsBusy(RuleRepairPhase phase) => phase is RuleRepairPhase.Scanning or RuleRepairPhase.Applying;

    /// <summary>
    /// No usable response: refused, reset, timed out, or a gateway (502, 503, 504) saying PoracleNG behind it
    /// did not answer. Any other status code means PoracleNG answered for that human.
    /// </summary>
    private static bool IsUnreachable(Exception ex) =>
        ex is TaskCanceledException
            or HttpRequestException
            {
                StatusCode: null or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout,
            };

    private void ResetUnreachable()
    {
        this._unreachableInARow = 0;
        this._unreachableReason = null;
    }

    /// <summary>
    /// Runs one PoracleNG call, counting responses and non-responses. The call that makes
    /// <see cref="MaxUnreachableInARow"/> in a row cancels <paramref name="abort"/>, which stops the run.
    /// </summary>
    private async Task<T> CallAsync<T>(Func<Task<T>> call, CancellationTokenSource abort)
    {
        abort.Token.ThrowIfCancellationRequested();
        try
        {
            var result = await call();
            Interlocked.Exchange(ref this._unreachableInARow, 0);
            return result;
        }
        catch (Exception ex) when (IsUnreachable(ex))
        {
            if (Interlocked.Increment(ref this._unreachableInARow) >= MaxUnreachableInARow)
            {
                Interlocked.CompareExchange(ref this._unreachableReason, ex.Message, null);
                await abort.CancelAsync();
            }

            throw;
        }
        catch
        {
            Interlocked.Exchange(ref this._unreachableInARow, 0);
            throw;
        }
    }

    private string UnreachableError(string stopped) =>
        $"PoracleNG could not be reached: {MaxUnreachableInARow} requests in a row got no response or a gateway error ({this._unreachableReason}). {stopped}";

    private async Task RunScanAsync()
    {
        using var abort = new CancellationTokenSource();
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
                new ParallelOptions { MaxDegreeOfParallelism = MaxHumansInFlight, CancellationToken = abort.Token },
                async (index, _) =>
                {
                    results[index] = await NamingTheHumanAsync(ids[index], abort, () => this.ScanHumanAsync(ids[index], abort));
                    this.Update(s => s with { HumansDone = s.HumansDone + 1 });
                });

            // Nobody read, for whatever mix of reasons: an empty list here would read as "nothing to repair".
            if (ids.Count > 0 && results.All(r => r.Skip is not null))
            {
                var (reason, count) = results
                    .GroupBy(r => r.Skip!.Reason, StringComparer.Ordinal)
                    .Select(g => (Reason: g.Key, Count: g.Count()))
                    .OrderByDescending(g => g.Count)
                    .ThenBy(g => g.Reason, StringComparer.Ordinal)
                    .First();
                this.Fail(
                    $"No user's rules could be read, so nothing was scanned. The most common reason ({count} of {ids.Count}): {reason}",
                    "scan");
                return;
            }

            var repairable = results.SelectMany(r => r.Repairable).OrderBy(i => i, ItemOrder.Instance).ToList();
            var cannotEdit = results.SelectMany(r => r.CannotEdit).OrderBy(i => i, ItemOrder.Instance).ToList();
            var skipped = results.Where(r => r.Skip is not null).Select(r => r.Skip!).OrderBy(s => s.HumanId, StringComparer.Ordinal).ToList();

            // The rows would otherwise name a user by Discord id alone; the list just read has their names.
            var listed = repairable.Select(i => i.HumanId).Concat(cannotEdit.Select(i => i.HumanId)).Concat(skipped.Select(s => s.HumanId))
                .ToHashSet(StringComparer.Ordinal);
            var names = humans
                .Where(h => !string.IsNullOrEmpty(h.Id) && listed.Contains(h.Id) && !string.IsNullOrWhiteSpace(h.Name))
                .GroupBy(h => h.Id, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First().Name!, StringComparer.Ordinal);

            this.Update(s => s with
            {
                Phase = RuleRepairPhase.Scanned,
                FinishedAt = DateTimeOffset.UtcNow,
                Repairable = repairable,
                CannotEdit = cannotEdit,
                Skipped = skipped,
                HumanNames = names,
            });

            LogScanned(this._logger, ids.Count, repairable.Count, cannotEdit.Count, skipped.Count);
        }
        catch (Exception) when (abort.IsCancellationRequested)
        {
            this.Fail(this.UnreachableError("The scan was stopped."), "scan");
        }
        catch (Exception ex)
        {
            this.Fail(ex.Message, "scan");
        }
    }

    /// <summary>
    /// What the server declares for <paramref name="type"/>, read once per human. The proxy caches the schema
    /// document itself, so this only saves the awaits.
    /// </summary>
    private static async Task<IReadOnlySet<string>?> DeclaredAsync(
        IPoracleTrackingProxy tracking, Dictionary<string, IReadOnlySet<string>?> declared, string type)
    {
        if (!declared.TryGetValue(type, out var properties))
        {
            properties = await tracking.GetDeclaredRulePropertiesAsync(type);
            declared[type] = properties;
        }

        return properties;
    }

    /// <summary>
    /// Reads one human's rules and classifies them, confirming any-tier candidates with a dry run. A failure
    /// that is about this human skips the human as a whole, so a half-read account never reports a partial
    /// list as if it were complete. Anything else propagates and fails the run.
    /// </summary>
    private async Task<HumanScan> ScanHumanAsync(string humanId, CancellationTokenSource abort)
    {
        var repairable = new List<RuleRepairItem>();
        var cannotEdit = new List<RuleRepairItem>();

        try
        {
            using var scope = this._scopes.CreateScope();
            var tracking = scope.ServiceProvider.GetRequiredService<IPoracleTrackingProxy>();
            var declared = new Dictionary<string, IReadOnlySet<string>?>(StringComparer.Ordinal);

            foreach (var rule in await this.CallAsync(() => tracking.GetStoredRulesAsync(humanId), abort))
            {
                var candidate = RuleRepairClassifier.Classify(rule, humanId, await DeclaredAsync(tracking, declared, rule.Type));
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
                        outcome = await this.CallAsync(
                            () => tracking.ReplaceStoredRuleAsync(item.Type, humanId, item.Uid, item.ProfileNo, row, dryRun: true), abort);
                    }
                    catch (AlarmValidationException ex)
                    {
                        cannotEdit.Add(item with { Kind = RuleRepairKind.CannotEdit, After = null, Reason = ex.Message });
                        continue;
                    }

                    // The repaired rule would be a copy of another one; list it rather than drop it.
                    if (outcome == StoredRuleReplaceOutcome.Conflict)
                    {
                        cannotEdit.Add(item with { Kind = RuleRepairKind.CannotEdit, After = null, Reason = DuplicateReason });
                        continue;
                    }

                    // Only "updated" says the stored rule is 9000/9000; anything else leaves nothing to repair.
                    if (outcome != StoredRuleReplaceOutcome.Updated)
                    {
                        continue;
                    }

                    item = item with { Before = StoredAnyTier };
                }

                repairable.Add(item);
            }

            return new HumanScan(repairable, cannotEdit, null);
        }
        catch (Exception ex) when (SkipReason(ex) is { } reason)
        {
            LogHumanSkipped(this._logger, LogSafe.Id(humanId), reason);
            return new HumanScan([], [], new RuleRepairSkip(humanId, reason));
        }
    }

    /// <summary>Why one human could not be read, or null when the failure is the whole run's.</summary>
    private static string? SkipReason(Exception ex) => ex switch
    {
        AccountGoneException => "The account no longer exists.",
        HttpRequestException { StatusCode: { } status } http => $"PoracleNG answered {(int)status}: {http.Message}",
        HttpRequestException http => $"PoracleNG could not be reached: {http.Message}",
        TaskCanceledException => "PoracleNG did not answer in time.",
        JsonException json => $"PoracleNG's answer could not be read: {json.Message}",
        PoracleAmbiguousAnswerException ambiguous => ambiguous.Message,
        _ => null,
    };

    private async Task RunApplyAsync()
    {
        using var abort = new CancellationTokenSource();
        try
        {
            IReadOnlyList<RuleRepairItem> repairable;
            lock (this._gate)
            {
                repairable = this._status.Repairable;
            }

            var byHuman = repairable.GroupBy(r => r.HumanId, StringComparer.Ordinal).ToList();
            var tallies = new ApplyTally[byHuman.Count];

            await Parallel.ForEachAsync(
                Enumerable.Range(0, byHuman.Count),
                new ParallelOptions { MaxDegreeOfParallelism = MaxHumansInFlight, CancellationToken = abort.Token },
                async (index, _) =>
                {
                    var humanId = byHuman[index].Key;
                    tallies[index] = await NamingTheHumanAsync(
                        humanId, abort, () => this.ApplyHumanAsync(humanId, byHuman[index].ToList(), abort));
                    this.Update(s => s with { HumansDone = s.HumansDone + 1 });
                });

            var result = new RuleRepairApplyResult(
                tallies.Sum(t => t.Repaired),
                tallies.Sum(t => t.AlreadyFine),
                tallies.Sum(t => t.Gone),
                tallies.SelectMany(t => t.Conflicts).OrderBy(i => i, ItemOrder.Instance).ToList(),
                tallies.SelectMany(t => t.Failed).OrderBy(i => i, ItemOrder.Instance).ToList());

            this.Update(s => s with { Phase = RuleRepairPhase.Applied, FinishedAt = DateTimeOffset.UtcNow, Applied = result });

            LogApplied(this._logger, result.Repaired, result.AlreadyFine, result.Conflicts.Count, result.Gone, result.Failed.Count);
        }
        catch (Exception) when (abort.IsCancellationRequested)
        {
            this.Fail(this.UnreachableError("The apply was stopped; repairs already written stay written."), "apply");
        }
        catch (Exception ex)
        {
            this.Fail(ex.Message, "apply");
        }
    }

    /// <summary>
    /// Repairs one human's items in turn. The human's rules are read again first, and each item is
    /// classified on what is stored now: a rule that has gone is Gone, one that no longer needs this repair
    /// is AlreadyFine, and the rest are dry-run and written from the fresh row, so nothing edited since the
    /// scan is reverted. A conflict is left as it is. The stored rule is logged just before each real write,
    /// so a repair can be undone from the log.
    /// </summary>
    private async Task<ApplyTally> ApplyHumanAsync(string humanId, List<RuleRepairItem> items, CancellationTokenSource abort)
    {
        var tally = new ApplyTally();
        using var scope = this._scopes.CreateScope();
        var tracking = scope.ServiceProvider.GetRequiredService<IPoracleTrackingProxy>();
        var declared = new Dictionary<string, IReadOnlySet<string>?>(StringComparer.Ordinal);

        Dictionary<(string Type, int ProfileNo, int Uid), StoredRule> stored;
        try
        {
            stored = [];
            foreach (var rule in await this.CallAsync(() => tracking.GetStoredRulesAsync(humanId), abort))
            {
                stored.TryAdd((rule.Type, rule.ProfileNo, rule.Uid), rule);
            }
        }
        catch (AccountGoneException)
        {
            tally.Gone += items.Count;
            return tally;
        }
        catch (Exception ex) when (!abort.IsCancellationRequested && IsItemFailure(ex))
        {
            LogRereadFailed(this._logger, LogSafe.Id(humanId), ex.Message);
            tally.Failed.AddRange(items.Select(i => i with { Reason = ex.Message }));
            return tally;
        }

        foreach (var item in items)
        {
            if (!stored.TryGetValue((item.Type, item.ProfileNo, item.Uid), out var rule))
            {
                tally.Gone++;
                continue;
            }

            var fresh = RuleRepairClassifier.Classify(rule, humanId, await DeclaredAsync(tracking, declared, rule.Type));
            if (fresh is null || fresh.Item.Kind != item.Kind || fresh.RepairedV1Row is not { } row)
            {
                tally.AlreadyFine++;
                continue;
            }

            try
            {
                var outcome = await this.CallAsync(
                    () => tracking.ReplaceStoredRuleAsync(item.Type, humanId, item.Uid, item.ProfileNo, row, dryRun: true), abort);
                if (outcome == StoredRuleReplaceOutcome.Updated)
                {
                    // 5.4.0 keeps the uid; nothing in PoracleWeb.NET tracks these rules by uid if it ever does not.
                    LogRepairWriting(this._logger, LogSafe.Id(humanId), item.Type, item.ProfileNo, item.Uid, item.Kind, rule.Rule);
                    outcome = await this.CallAsync(
                        () => tracking.ReplaceStoredRuleAsync(item.Type, humanId, item.Uid, item.ProfileNo, row, dryRun: false), abort);
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
                        tally.Conflicts.Add(item with { Reason = DuplicateReason });
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
            catch (Exception ex) when (!abort.IsCancellationRequested && IsItemFailure(ex))
            {
                LogRepairFailed(this._logger, item.Type, item.Uid, LogSafe.Id(humanId), ex.Message);
                tally.Failed.Add(item with { Reason = ex.Message });
            }
        }

        return tally;
    }

    /// <summary>
    /// Runs one human's work and, when something unexpected fails the run there, says whose rules were being
    /// processed. <see cref="PoracleUnsupportedException"/> is about the server, not the human, and a stopped
    /// run has its own message, so both pass through as they are.
    /// </summary>
    private static async Task<T> NamingTheHumanAsync<T>(string humanId, CancellationTokenSource abort, Func<Task<T>> run)
    {
        try
        {
            return await run();
        }
        catch (Exception ex) when (!abort.IsCancellationRequested && ex is not (PoracleUnsupportedException or OperationCanceledException))
        {
            throw new RuleRepairHumanFailedException(HumanLabel(humanId), ex);
        }
    }

    /// <summary>
    /// How a human is named in a failure the admin page shows. A webhook's id is its URL, which carries its
    /// token, so it is never printed.
    /// </summary>
    internal static string HumanLabel(string humanId) =>
        Uri.TryCreate(humanId, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? "a webhook"
            : $"user {humanId}";

    /// <summary>An unexpected failure while processing one human, which fails the whole run.</summary>
    private sealed class RuleRepairHumanFailedException(string human, Exception inner)
        : Exception($"{inner.Message} (while processing {human})", inner);

    /// <summary>
    /// Failures that belong to one rule (or one human's read). <see cref="PoracleUnsupportedException"/> and
    /// anything unexpected are the run's, and propagate.
    /// </summary>
    private static bool IsItemFailure(Exception ex) =>
        ex is HttpRequestException or TaskCanceledException or JsonException or PoracleAmbiguousAnswerException or AlarmValidationException;

    private void Update(Func<RuleRepairStatus, RuleRepairStatus> change)
    {
        lock (this._gate)
        {
            this._status = change(this._status);
        }
    }

    private void Fail(string error, string run)
    {
        LogRunFailed(this._logger, run, error);
        this.Update(s => s with { Phase = RuleRepairPhase.Failed, FinishedAt = DateTimeOffset.UtcNow, Error = error });
    }

    private sealed record HumanScan(
        IReadOnlyList<RuleRepairItem> Repairable,
        IReadOnlyList<RuleRepairItem> CannotEdit,
        RuleRepairSkip? Skip);

    private sealed class ApplyTally
    {
        public int Repaired { get; set; }
        public int AlreadyFine { get; set; }
        public List<RuleRepairItem> Conflicts { get; } = [];
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
        Level = LogLevel.Information,
        Message = "Rule repair writing human {HumanId} {Type} profile {ProfileNo} uid {Uid} ({Kind}); the stored v2 rule it replaces: {StoredRule}")]
    private static partial void LogRepairWriting(
        ILogger logger, string humanId, string type, int profileNo, int uid, RuleRepairKind kind, JsonElement storedRule);

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
        Message = "Rule repair could not re-read human {HumanId}, so none of their rules were repaired: {Reason}")]
    private static partial void LogRereadFailed(ILogger logger, string humanId, string reason);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Rule repair {Run} failed: {Reason}")]
    private static partial void LogRunFailed(ILogger logger, string run, string reason);
}
