using Microsoft.Extensions.Logging;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

/// <summary>
/// Moves an alarm from one human of a linked pair to the other. The order is the guarantee: every check
/// runs before anything is written, the target is created before the source is deleted, and a failure
/// between the two leaves the alert on both sides rather than on neither.
/// </summary>
public partial class AlarmMover(
    ILinkedAccount linked,
    IHumanService humans,
    IPoracleHumanProxy humanProxy,
    IUserAreaPolicy areaPolicy,
    QuickPickUidTransfer uidTransfer,
    ILogger<AlarmMover> logger)
{
    internal const string DifferentProfiles = "Your Discord and Telegram sides are on different profiles. Resolve the banner first.";

    private readonly IUserAreaPolicy _areaPolicy = areaPolicy;
    private readonly IPoracleHumanProxy _humanProxy = humanProxy;
    private readonly IHumanService _humans = humans;
    private readonly ILinkedAccount _linked = linked;
    private readonly ILogger<AlarmMover> _logger = logger;
    private readonly QuickPickUidTransfer _uidTransfer = uidTransfer;

    /// <summary>
    /// Moves <paramref name="model"/> (the alarm as it should be on the target, edits included) from
    /// <paramref name="sourceOwnerId"/> to the <paramref name="target"/> side of the caller's pair.
    /// </summary>
    /// <remarks>
    /// The profile is never read from <paramref name="model"/>: a v2 read carries no <c>profile_no</c>, so it is
    /// always 0. A rule lives on its human's active profile, so the source human's current profile is the
    /// rule's, and the target must be on the same one.
    /// </remarks>
    /// <exception cref="HttpRequestException">PoracleNG could not be reached. Propagates so the caller can answer 503.</exception>
    public async Task<MoveResult<TModel>> MoveAsync<TModel>(
        IAlarmService<TModel> svc, string trackingType, string userId, TModel model, string sourceOwnerId, LinkSide target)
        where TModel : class, IHasDestination
    {
        var uid = model.Uid;
        var pair = await this._linked.ResolveAsync(userId);
        if (!pair.IsLinked)
        {
            return Refused<TModel>(uid, "Your accounts aren't linked.");
        }

        if (pair.SideOf(sourceOwnerId) is not { } sourceSide)
        {
            return Refused<TModel>(uid, "That alert doesn't belong to your linked accounts.");
        }

        if (sourceSide == target)
        {
            return Refused<TModel>(uid, "That alert is already there.");
        }

        var targetSide = target == LinkSide.Discord ? pair.Discord : pair.Telegram;
        if (targetSide is not { Exists: true })
        {
            return Refused<TModel>(uid, target == LinkSide.Discord ? "Discord isn't set up yet." : "Telegram isn't set up yet.");
        }

        var targetId = targetSide.HumanId;

        var preflight = await this.PreflightAsync(model, sourceOwnerId, targetId, target);
        if (preflight.Refusal is not null)
        {
            return Refused<TModel>(uid, preflight.Refusal);
        }

        var profileNo = preflight.ProfileNo;

        // A stamp from a linked read is ours, not the rule's. The uid is cleared because a service hands back the
        // model it was given when nothing was written, and a leftover source uid would read as a new rule.
        model.Destination = null;
        model.PausedDestination = null;
        model.Uid = 0;
        model.ExistingUid = null;

        TModel created;
        try
        {
            created = await svc.CreateAsync(targetId, model);
        }
        catch (TrackingConflictException ex) when (ex.IsIdentical && ex.ExistingUid is { } existingUid)
        {
            // An exact repeat already on the target is what the person wanted: the alert is there.
            model.Uid = existingUid;
            created = model;
        }
        catch (TrackingConflictException ex)
        {
            return new MoveResult<TModel>(
                new MoveOutcome(uid, MoveStatus.Conflict, ExistingUid: ex.ExistingUid, Reason: ex.Message), Conflict: ex);
        }
        catch (AlarmValidationException ex)
        {
            return Refused<TModel>(uid, ex.Message);
        }
        catch (PoracleRequestRefusedException ex)
        {
            return Refused<TModel>(uid, ex.Message);
        }
        catch (Exception ex) when (ex is not (HttpRequestException or OperationCanceledException))
        {
            LogCreateFailed(this._logger, ex, trackingType, uid);
            return new MoveResult<TModel>(new MoveOutcome(uid, MoveStatus.Failed,
                Reason: "That alert may or may not have been copied. Look at both sides before trying again."));
        }

        // Uid 0 means an identical rule was already on the target; map the source onto that rule when PoracleNG named it.
        int? newUid = created.Uid > 0 ? created.Uid : created.ExistingUid is > 0 ? created.ExistingUid : null;
        if (created.Uid <= 0 && newUid is { } mapped)
        {
            created.Uid = mapped;
        }

        var deleteError = await this.DeleteSourceAsync(svc, sourceOwnerId, uid);
        if (deleteError is not null)
        {
            LogSourceRemains(this._logger, trackingType, uid, deleteError);
            return new MoveResult<TModel>(
                new MoveOutcome(uid, MoveStatus.SourceRemains, newUid,
                    Reason: "It was copied, but the original couldn't be removed. Delete it from the side it came from."),
                created);
        }

        if (newUid is { } followed)
        {
            try
            {
                await this._uidTransfer.TransferAsync(sourceOwnerId, targetId, profileNo, trackingType, uid, followed);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The alert moved. Losing the pick's bookkeeping only costs a stale "remove" on one quick pick.
                LogTransferFailed(this._logger, ex, trackingType, uid, followed);
            }
        }

        return new MoveResult<TModel>(new MoveOutcome(uid, MoveStatus.Moved, newUid), created);
    }

    /// <summary>
    /// Moves several of the caller's alarms to <paramref name="target"/>. One list read per human maps every
    /// uid to its owner, as the router does for bulk distance. A uid neither human has is refused. A PoracleNG
    /// outage propagates (the caller answers 503) rather than becoming a row per alert.
    /// </summary>
    public async Task<IReadOnlyList<MoveOutcome>> MoveManyAsync<TModel>(
        IAlarmService<TModel> svc, string trackingType, string userId, int profileNo, IReadOnlyList<int> uids, LinkSide target)
        where TModel : class, IHasDestination
    {
        var pair = await this._linked.ResolveAsync(userId);
        if (!pair.IsLinked)
        {
            return [.. uids.Select(u => Refused<TModel>(u, "Your accounts aren't linked.").Outcome)];
        }

        var found = new Dictionary<int, (TModel Model, string Owner)>();
        foreach (var humanId in pair.ExistingHumanIds)
        {
            foreach (var model in await svc.GetByUserAsync(humanId, profileNo))
            {
                found.TryAdd(model.Uid, (model, humanId));
            }
        }

        var results = new List<MoveOutcome>(uids.Count);
        foreach (var uid in uids)
        {
            results.Add(found.TryGetValue(uid, out var hit)
                ? (await this.MoveAsync(svc, trackingType, userId, hit.Model, hit.Owner, target)).Outcome
                : Refused<TModel>(uid, "That alert wasn't found.").Outcome);
        }

        return results;
    }

    private sealed record Preflight(string? Refusal, int ProfileNo);

    private async Task<Preflight> PreflightAsync(IHasDestination model, string sourceId, string targetId, LinkSide target)
    {
        var side = target == LinkSide.Discord ? "Discord" : "Telegram";
        var targetHuman = await this._humans.GetByIdAsync(targetId);
        var sourceHuman = await this._humans.GetByIdAsync(sourceId);
        if (targetHuman is null || sourceHuman is null)
        {
            return new Preflight($"{side} isn't set up yet.", 0);
        }

        if (targetHuman.CurrentProfileNo != sourceHuman.CurrentProfileNo)
        {
            return new Preflight(DifferentProfiles, 0);
        }

        var label = model.OverrideLocationLabel;
        if (!string.IsNullOrEmpty(label))
        {
            var places = await this._humanProxy.GetPlacesAsync(targetId);
            if (!places.Named.Any(p => string.Equals(p.Label, label, StringComparison.OrdinalIgnoreCase)))
            {
                return new Preflight($"The place \"{label}\" isn't saved on {side}. Save it there first.", 0);
            }
        }

        if (model.OverrideAreas is { Count: > 0 } areas)
        {
            try
            {
                await this._areaPolicy.EnsureOverrideAreasAllowedAsync(targetId, areas, null);
            }
            catch (AlarmValidationException ex)
            {
                return new Preflight(ex.Message, 0);
            }
        }

        return new Preflight(null, sourceHuman.CurrentProfileNo);
    }

    private async Task<string?> DeleteSourceAsync<TModel>(IAlarmService<TModel> svc, string sourceOwnerId, int uid)
    {
        string? last = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                await svc.DeleteAsync(sourceOwnerId, uid);
                return null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                last = ex.Message;
            }
        }

        return last;
    }

    private static MoveResult<TModel> Refused<TModel>(int uid, string reason)
        where TModel : class =>
        new(new MoveOutcome(uid, MoveStatus.Refused, Reason: reason));

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Moved {TrackingType} alarm {Uid} was created on the target but the source could not be deleted: {Error}")]
    private static partial void LogSourceRemains(ILogger logger, string trackingType, int uid, string error);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Moved {TrackingType} alarm {OldUid} to {NewUid}, but quick-pick tracking could not follow it.")]
    private static partial void LogTransferFailed(ILogger logger, Exception exception, string trackingType, int oldUid, int newUid);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Creating moved {TrackingType} alarm {Uid} on the target failed unexpectedly; it may or may not exist there.")]
    private static partial void LogCreateFailed(ILogger logger, Exception exception, string trackingType, int uid);
}
