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
    public async Task<MoveOutcome> MoveAsync<TModel>(
        IAlarmService<TModel> svc, string trackingType, string userId, TModel model, string sourceOwnerId, LinkSide target)
        where TModel : class, IHasDestination
    {
        // PoracleNG may write the new uid back onto the model it is handed, so the source's is read first.
        var uid = model.Uid;
        var pair = await this._linked.ResolveAsync(userId);
        if (!pair.IsLinked)
        {
            return Refused(uid, "Your accounts aren't linked.");
        }

        if (pair.SideOf(sourceOwnerId) is not { } sourceSide)
        {
            return Refused(uid, "That alert doesn't belong to your linked accounts.");
        }

        if (sourceSide == target)
        {
            return Refused(uid, "That alert is already there.");
        }

        var targetSide = target == LinkSide.Discord ? pair.Discord : pair.Telegram;
        if (targetSide is not { Exists: true })
        {
            return Refused(uid, target == LinkSide.Discord ? "Discord isn't set up yet." : "Telegram isn't set up yet.");
        }

        var targetId = targetSide.HumanId;

        var refusal = await this.PreflightAsync(model, targetId, target);
        if (refusal is not null)
        {
            return Refused(uid, refusal);
        }

        // A stamp from a linked read is ours, not the rule's; the target's stamp is applied on the way out.
        model.Destination = null;
        model.PausedDestination = null;

        int? newUid;
        try
        {
            var created = await svc.CreateAsync(targetId, model);
            // Uid 0: PoracleNG found an identical rule already on the target, so there is nothing to follow.
            newUid = created.Uid > 0 ? created.Uid : null;
        }
        catch (TrackingConflictException ex)
        {
            return new MoveOutcome(uid, MoveStatus.Conflict, ExistingUid: ex.ExistingUid, Reason: ex.Message);
        }
        catch (AlarmValidationException ex)
        {
            return Refused(uid, ex.Message);
        }

        var deleteError = await this.DeleteSourceAsync(svc, sourceOwnerId, uid);
        if (deleteError is not null)
        {
            LogSourceRemains(this._logger, trackingType, uid, deleteError);
            return new MoveOutcome(uid, MoveStatus.SourceRemains, newUid,
                Reason: "It was copied, but the original couldn't be removed. Delete it from the side it came from.");
        }

        if (newUid is { } created2)
        {
            try
            {
                await this._uidTransfer.TransferAsync(sourceOwnerId, targetId, model.ProfileNo, trackingType, uid, created2);
            }
            catch (Exception ex)
            {
                // The alert moved. Losing the pick's bookkeeping only costs a stale "remove" on one quick pick.
                LogTransferFailed(this._logger, ex, trackingType, uid, created2);
            }
        }

        return new MoveOutcome(uid, MoveStatus.Moved, newUid);
    }

    /// <summary>
    /// Moves several of the caller's alarms to <paramref name="target"/>. One list read per human maps every
    /// uid to its owner, as the router does for bulk distance. A uid neither human has is refused.
    /// </summary>
    public async Task<IReadOnlyList<MoveOutcome>> MoveManyAsync<TModel>(
        IAlarmService<TModel> svc, string trackingType, string userId, int profileNo, IReadOnlyList<int> uids, LinkSide target)
        where TModel : class, IHasDestination
    {
        var pair = await this._linked.ResolveAsync(userId);
        if (!pair.IsLinked)
        {
            return [.. uids.Select(u => Refused(u, "Your accounts aren't linked."))];
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
            if (!found.TryGetValue(uid, out var hit))
            {
                results.Add(Refused(uid, "That alert wasn't found."));
                continue;
            }

            try
            {
                results.Add(await this.MoveAsync(svc, trackingType, userId, hit.Model, hit.Owner, target));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One alert failing must not hide what happened to the others.
                LogMoveFailed(this._logger, ex, trackingType, uid);
                results.Add(Refused(uid, "That alert couldn't be moved. Check both sides before trying again."));
            }
        }

        return results;
    }

    private async Task<string?> PreflightAsync(IHasDestination model, string targetId, LinkSide target)
    {
        var human = await this._humans.GetByIdAsync(targetId);
        if (human is null)
        {
            return target == LinkSide.Discord ? "Discord isn't set up yet." : "Telegram isn't set up yet.";
        }

        if (human.CurrentProfileNo != model.ProfileNo)
        {
            return DifferentProfiles;
        }

        var label = model.OverrideLocationLabel;
        if (!string.IsNullOrEmpty(label))
        {
            var places = await this._humanProxy.GetPlacesAsync(targetId);
            if (!places.Named.Any(p => string.Equals(p.Label, label, StringComparison.OrdinalIgnoreCase)))
            {
                var side = target == LinkSide.Discord ? "Discord" : "Telegram";
                return $"The place \"{label}\" isn't saved on {side}. Save it there first.";
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
                return ex.Message;
            }
        }

        return null;
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

    private static MoveOutcome Refused(int uid, string reason) => new(uid, MoveStatus.Refused, Reason: reason);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Moved {TrackingType} alarm {Uid} was created on the target but the source could not be deleted: {Error}")]
    private static partial void LogSourceRemains(ILogger logger, string trackingType, int uid, string error);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Moved {TrackingType} alarm {OldUid} to {NewUid}, but quick-pick tracking could not follow it.")]
    private static partial void LogTransferFailed(ILogger logger, Exception exception, string trackingType, int oldUid, int newUid);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Moving {TrackingType} alarm {Uid} failed before anything was reported.")]
    private static partial void LogMoveFailed(ILogger logger, Exception exception, string trackingType, int uid);
}
