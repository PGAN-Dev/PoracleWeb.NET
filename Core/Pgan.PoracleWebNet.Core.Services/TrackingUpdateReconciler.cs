using Microsoft.Extensions.Logging;
using Pgan.PoracleWebNet.Core.Abstractions.Services;

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
        // oldUid <= 0 means this was not an edit. No insert, or the same uid back, means the upsert worked.
        if (oldUid <= 0 || result.Inserts <= 0 || result.NewUids.Count == 0)
        {
            return oldUid;
        }

        var newUid = (int)result.NewUids[0];
        if (newUid == oldUid)
        {
            return oldUid;
        }

        try
        {
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
