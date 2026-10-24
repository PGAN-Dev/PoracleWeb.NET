using System.Text.Json;
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
    IPoracleTrackingProxy tracking,
    IUserAreaPolicy areaPolicy,
    QuickPickUidTransfer uidTransfer,
    ILogger<AlarmMover> logger)
{
    internal const string DifferentProfiles = "Discord and Telegram are on different profiles. Switch one to match, then try again.";

    private readonly IUserAreaPolicy _areaPolicy = areaPolicy;
    private readonly IPoracleHumanProxy _humanProxy = humanProxy;
    private readonly IHumanService _humans = humans;
    private readonly ILinkedAccount _linked = linked;
    private readonly ILogger<AlarmMover> _logger = logger;
    private readonly IPoracleTrackingProxy _tracking = tracking;
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
        var session = new Session(this, await this._linked.ResolveAsync(userId));
        return await this.MoveCoreAsync(session, svc, trackingType, model, sourceOwnerId, target);
    }

    private async Task<MoveResult<TModel>> MoveCoreAsync<TModel>(
        Session session, IAlarmService<TModel> svc, string trackingType, TModel model, string sourceOwnerId, LinkSide target)
        where TModel : class, IHasDestination
    {
        var uid = model.Uid;
        var pair = session.Pair;
        if (!pair.IsLinked)
        {
            return Refused<TModel>(uid, "Your accounts aren't linked.", MoveReasonCodes.NotLinked);
        }

        if (pair.SideOf(sourceOwnerId) is not { } sourceSide)
        {
            return Refused<TModel>(uid, "That alarm doesn't belong to your linked accounts.", MoveReasonCodes.NotYours);
        }

        if (sourceSide == target)
        {
            // Already where it was asked to go: what the person wanted is true, so it is reported as done, not refused.
            return new MoveResult<TModel>(new MoveOutcome(uid, MoveStatus.AlreadyThere, Reason: "That alarm is already there.",
                ReasonCode: MoveReasonCodes.AlreadyThere, ReasonApp: WireName(target)));
        }

        var targetSide = target == LinkSide.Discord ? pair.Discord : pair.Telegram;
        if (targetSide is not { Exists: true })
        {
            return Refused<TModel>(uid, $"{SideName(target)} isn't set up yet.", MoveReasonCodes.NotSetUp, target);
        }

        var targetId = targetSide.HumanId;

        var preflight = await this.PreflightAsync(session, model, sourceOwnerId, sourceSide, targetId, target);
        if (preflight.Refusal is not null)
        {
            return new MoveResult<TModel>(new MoveOutcome(uid, MoveStatus.Refused, Reason: preflight.Refusal,
                ReasonCode: preflight.Code, ReasonApp: preflight.App is { } app ? WireName(app) : null, ReasonPlace: preflight.Place));
        }

        var profileNo = preflight.ProfileNo;

        await this.CarryUnmodelledAsync(session, trackingType, sourceOwnerId, model);

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
                new MoveOutcome(uid, MoveStatus.Conflict, ExistingUid: ex.ExistingUid, Reason: ex.Message,
                    ReasonCode: MoveReasonCodes.Conflict, ReasonApp: WireName(target)), Conflict: ex);
        }
        catch (AlarmValidationException ex)
        {
            return Refused<TModel>(uid, ex.Message, MoveReasonCodes.Invalid, target);
        }
        catch (PoracleRequestRefusedException ex)
        {
            return Refused<TModel>(uid, ex.Message, MoveReasonCodes.Invalid, target);
        }
        // A switched-off type and a too-old PoracleNG have their own filters (403, 409); they say nothing about
        // whether a copy landed, because they refuse before anything is written.
        catch (Exception ex) when (ex is not (HttpRequestException or OperationCanceledException
            or FeatureDisabledException or PoracleUnsupportedException))
        {
            LogCreateFailed(this._logger, ex, trackingType, uid);
            return new MoveResult<TModel>(new MoveOutcome(uid, MoveStatus.Failed,
                Reason: "That alarm may or may not have been copied. Look at both sides before trying again.",
                ReasonCode: MoveReasonCodes.Failed, ReasonApp: WireName(target)));
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
                    Reason: "It was copied, but the original couldn't be removed. Delete it from the side it came from.",
                    ReasonCode: MoveReasonCodes.SourceRemains, ReasonApp: WireName(target)),
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
            return [.. uids.Select(u => Refused<TModel>(u, "Your accounts aren't linked.", MoveReasonCodes.NotLinked).Outcome)];
        }

        var found = new Dictionary<int, (TModel Model, string Owner)>();
        foreach (var humanId in pair.ExistingHumanIds)
        {
            foreach (var model in await svc.GetByUserAsync(humanId, profileNo))
            {
                found.TryAdd(model.Uid, (model, humanId));
            }
        }

        // The pair, both humans, the target's places and the source rows are read once for the request.
        var session = new Session(this, pair);
        var results = new List<MoveOutcome>(uids.Count);
        foreach (var uid in uids)
        {
            results.Add(found.TryGetValue(uid, out var hit)
                ? (await this.MoveCoreAsync(session, svc, trackingType, hit.Model, hit.Owner, target)).Outcome
                : Refused<TModel>(uid, "That alarm wasn't found.", MoveReasonCodes.NotFound).Outcome);
        }

        return results;
    }

    private sealed record Preflight(string? Refusal, int ProfileNo, string? Code = null, LinkSide? App = null, string? Place = null);

    private async Task<Preflight> PreflightAsync(
        Session session, IHasDestination model, string sourceId, LinkSide source, string targetId, LinkSide target)
    {
        var side = SideName(target);
        var targetHuman = await session.HumanAsync(targetId);
        if (targetHuman is null)
        {
            return new Preflight($"{side} isn't set up yet.", 0, MoveReasonCodes.NotSetUp, target);
        }

        var sourceHuman = await session.HumanAsync(sourceId);
        if (sourceHuman is null)
        {
            return new Preflight($"{SideName(source)} isn't set up yet.", 0, MoveReasonCodes.NotSetUp, source);
        }

        if (targetHuman.CurrentProfileNo != sourceHuman.CurrentProfileNo)
        {
            return new Preflight(DifferentProfiles, 0, MoveReasonCodes.DifferentProfiles);
        }

        var label = model.OverrideLocationLabel;
        if (!string.IsNullOrEmpty(label))
        {
            var places = await session.PlacesAsync(targetId);
            if (!places.Named.Any(p => string.Equals(p.Label, label, StringComparison.OrdinalIgnoreCase)))
            {
                return new Preflight($"The place \"{label}\" isn't saved on {side}. Save it there first.", 0, MoveReasonCodes.PlaceMissing, target, label);
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
                return new Preflight(ex.Message, 0, MoveReasonCodes.AreaNotAllowed, target);
            }
        }

        return new Preflight(null, sourceHuman.CurrentProfileNo);
    }

    /// <summary>
    /// Attaches the source row's properties the typed model has no field for, so the create keeps them the way an edit
    /// does (<see cref="TrackingFieldPreserver"/>). The row's own uid and id stay behind: the create is a new rule on
    /// another human. Pokéstop events have no untyped path to carry anything through, on edit either.
    /// </summary>
    private async Task CarryUnmodelledAsync<TModel>(Session session, string trackingType, string sourceOwnerId, TModel model)
        where TModel : class, IHasDestination
    {
        if (trackingType == "incident" || await session.RowsAsync(trackingType, sourceOwnerId) is not { } rows)
        {
            return;
        }

        var body = PoracleJsonHelper.SerializeToElement(model);
        var merged = TrackingFieldPreserver.PreserveStoredFields(trackingType, rows, model.Uid, body);
        var extras = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var prop in merged.EnumerateObject())
        {
            if (!body.TryGetProperty(prop.Name, out _) && prop.Name is not ("uid" or "id"))
            {
                extras[prop.Name] = prop.Value;
            }
        }

        if (extras.Count > 0)
        {
            CarriedFields.Attach(model, JsonSerializer.SerializeToElement(extras));
        }
    }

    /// <summary>What one request has already read, so a bulk move asks PoracleNG once per human rather than once per alarm.</summary>
    private sealed class Session(AlarmMover owner, LinkedPair pair)
    {
        private readonly Dictionary<string, Human?> _humans = new(StringComparer.Ordinal);
        private readonly Dictionary<string, SavedPlaces> _places = new(StringComparer.Ordinal);
        private readonly Dictionary<(string Type, string Owner), JsonElement?> _rows = [];

        public LinkedPair Pair { get; } = pair;

        public async Task<Human?> HumanAsync(string id)
        {
            if (!this._humans.TryGetValue(id, out var human))
            {
                this._humans[id] = human = await owner._humans.GetByIdAsync(id);
            }

            return human;
        }

        public async Task<SavedPlaces> PlacesAsync(string id)
        {
            if (!this._places.TryGetValue(id, out var places))
            {
                this._places[id] = places = await owner._humanProxy.GetPlacesAsync(id);
            }

            return places;
        }

        /// <summary>The owner's stored rows of a type, or null when they could not be read (the move then goes ahead on the model alone).</summary>
        public async Task<JsonElement?> RowsAsync(string type, string ownerId)
        {
            if (!this._rows.TryGetValue((type, ownerId), out var rows))
            {
                try
                {
                    rows = await owner._tracking.GetByUserAsync(type, ownerId);
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                {
                    rows = null;
                }

                this._rows[(type, ownerId)] = rows;
            }

            return rows;
        }
    }

    private static string SideName(LinkSide side) => side == LinkSide.Discord ? "Discord" : "Telegram";

    private static string WireName(LinkSide side) => side == LinkSide.Discord ? "discord" : "telegram";

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
            // What a delete throws when PoracleNG did not remove the rule. Anything else is a bug and propagates.
            catch (Exception ex) when (ex is HttpRequestException or PoracleRequestRefusedException
                or PoracleUnsupportedException or PoracleAmbiguousAnswerException or AccountGoneException
                or JsonException)
            {
                last = ex.Message;
            }
        }

        return last;
    }

    private static MoveResult<TModel> Refused<TModel>(int uid, string reason, string code, LinkSide? app = null)
        where TModel : class =>
        new(new MoveOutcome(uid, MoveStatus.Refused, Reason: reason, ReasonCode: code, ReasonApp: app is { } side ? WireName(side) : null));

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Moved {TrackingType} alarm {Uid} was created on the target but the source could not be deleted: {Error}")]
    private static partial void LogSourceRemains(ILogger logger, string trackingType, int uid, string error);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Moved {TrackingType} alarm {OldUid} to {NewUid}, but quick-pick tracking could not follow it.")]
    private static partial void LogTransferFailed(ILogger logger, Exception exception, string trackingType, int oldUid, int newUid);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Creating moved {TrackingType} alarm {Uid} on the target failed unexpectedly; it may or may not exist there.")]
    private static partial void LogCreateFailed(ILogger logger, Exception exception, string trackingType, int uid);
}
