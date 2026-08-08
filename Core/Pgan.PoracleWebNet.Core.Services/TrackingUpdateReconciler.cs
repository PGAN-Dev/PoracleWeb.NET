using Microsoft.Extensions.Logging;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

/// <summary>
/// Reconciles a tracking update against what PoracleNG actually did.
/// <para>
/// Updates are sent as a create carrying the existing <c>uid</c>, which PoracleNG normally treats as an
/// upsert. But it dedups each tracking type by a natural key (egg level, raid team/exclusive, quest reward,
/// lure id, and so on), so when an edit changes a field in that key it <em>inserts a new row</em> instead of
/// updating the one the uid points at. The original row survives and the user ends up with two alarms —
/// one still matching their pre-edit filter — while the API reports success.
/// </para>
/// <para>
/// This detects that case from the create response and removes the superseded row. The upsert path is left
/// alone, so the uid only changes when PoracleNG genuinely made a new row.
/// </para>
/// </summary>
internal static partial class TrackingUpdateReconciler
{
    /// <summary>
    /// Deletes the superseded row when PoracleNG inserted rather than updated.
    /// Returns the uid the caller should report back — the new one when a row was inserted, otherwise the original.
    /// </summary>
    public static async Task<int> ReconcileAsync(
        IPoracleTrackingProxy proxy,
        string trackingType,
        string userId,
        int oldUid,
        TrackingCreateResult result,
        ILogger logger,
        ITrackedUidRemapper? uidRemapper = null)
    {
        // oldUid <= 0 means this was not an edit, so there is nothing to reconcile.
        if (oldUid <= 0)
        {
            return oldUid;
        }

        // PoracleNG declined to write because the edited values collide with an alarm the user
        // already has. Nothing changed, so reporting success while echoing the requested values back
        // told the user their edit applied when it had not. See #463.
        if (result.AlreadyPresent > 0 && result.Inserts == 0 && result.Updates == 0)
        {
            throw new TrackingConflictException(
                trackingType,
                "Another alarm of this type already uses those settings. Edit or remove that one instead.");
        }

        if (result.NewUids.Count == 0)
        {
            return oldUid;
        }

        // Trust newUids, not the insert counter. PoracleNG re-keys a row on edit while reporting
        // {"insert":0,"updates":1,"newUids":[<new>]} -- verified directly against it. Gating on
        // Inserts > 0 therefore skipped both the uid correction and the remap for raids, eggs,
        // quests, gyms, fort changes and nests: the PUT answered 200 with a uid that 404s on the very
        // next GET, and any quick pick tracking the row kept pointing at the dead uid. Monsters were
        // unaffected because PoracleNG genuinely updates that type in place. See #460, #464.
        var newUid = (int)result.NewUids[0];
        if (newUid == oldUid)
        {
            return oldUid;
        }

        try
        {
            // Idempotent: when PoracleNG replaced the row in place rather than inserting a duplicate,
            // the old uid is already gone and the proxy swallows the resulting 404.
            await proxy.DeleteByUidAsync(trackingType, userId, oldUid);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // The new row already carries the user's intended settings, so the edit succeeded. Surface the
            // leftover duplicate for triage rather than failing an update that actually applied.
            LogStaleDeleteFailed(logger, ex, trackingType, oldUid, newUid);
        }

        // Quick-pick applied state stores uids captured at apply time; follow the row. See #403.
        if (uidRemapper != null)
        {
            await uidRemapper.RemapAsync(userId, trackingType, oldUid, newUid);
        }

        return newUid;
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Failed to delete superseded {TrackingType} uid {OldUid} after the edit created uid {NewUid}; a duplicate row may remain.")]
    private static partial void LogStaleDeleteFailed(ILogger logger, Exception exception, string trackingType, int oldUid, int newUid);
}
