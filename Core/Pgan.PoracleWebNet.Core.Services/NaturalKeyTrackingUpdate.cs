using System.Text.Json;
using Microsoft.Extensions.Logging;
using Pgan.PoracleWebNet.Core.Abstractions.Services;

namespace Pgan.PoracleWebNet.Core.Services;

/// <summary>
/// Update strategy for the tracking types PoracleNG protects with a natural unique key —
/// <c>lure_tracking(id, profile_no, lure_id)</c> and
/// <c>invasion_tracking(id, profile_no, gender, grunt_type)</c>.
/// <para>
/// PoracleNG's create endpoint has no upsert path for these: it treats a row as "already present" only when
/// every field matches, so changing a field <em>outside</em> the natural key makes it attempt an INSERT that
/// collides with the existing row. MariaDB raises
/// <c>Error 1062 Duplicate entry '&lt;id&gt;-&lt;profile&gt;-&lt;key&gt;'</c>, PoracleNG answers
/// <c>500 {"message":"database error"}</c>, and the user's edit is silently discarded. Editing the distance
/// or template of a lure was therefore impossible.
/// </para>
/// <para>
/// The other types carry no natural unique index (only <c>PRIMARY(uid)</c>), so their creates cannot collide
/// and <see cref="TrackingUpdateReconciler"/> handles them.
/// </para>
/// </summary>
internal static partial class NaturalKeyTrackingUpdate
{
    /// <summary>
    /// Replaces the existing row: delete first so the natural key is free, then create.
    /// <para>
    /// If the create fails the original is restored, so a failed edit leaves the alarm intact rather than
    /// destroying it — the risk inherent in a bare delete-then-create.
    /// </para>
    /// </summary>
    /// <returns>The uid of the surviving row.</returns>
    public static async Task<int> ReplaceAsync(
        IPoracleTrackingProxy proxy,
        string trackingType,
        string userId,
        int oldUid,
        JsonElement? original,
        JsonElement updated,
        ILogger logger)
    {
        // Not an edit: nothing to free up, so this is an ordinary create.
        if (oldUid <= 0)
        {
            var created = await proxy.CreateAsync(trackingType, userId, updated);
            return created.NewUids.Count > 0 ? (int)created.NewUids[0] : oldUid;
        }

        await proxy.DeleteByUidAsync(trackingType, userId, oldUid);

        try
        {
            var result = await proxy.CreateAsync(trackingType, userId, updated);
            return result.NewUids.Count > 0 ? (int)result.NewUids[0] : oldUid;
        }
        catch (Exception ex)
        {
            LogReplaceFailed(logger, ex, trackingType, oldUid);

            // Put the original back so the edit fails without losing the user's alarm.
            if (original.HasValue)
            {
                try
                {
                    await proxy.CreateAsync(trackingType, userId, original.Value);
                }
                catch (Exception restoreEx)
                {
                    LogRestoreFailed(logger, restoreEx, trackingType, oldUid);
                }
            }

            throw;
        }
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Re-creating {TrackingType} uid {OldUid} failed after its row was removed for the edit; restoring the original.")]
    private static partial void LogReplaceFailed(ILogger logger, Exception exception, string trackingType, int oldUid);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Could not restore the original {TrackingType} uid {OldUid} after a failed edit; the alarm is gone.")]
    private static partial void LogRestoreFailed(ILogger logger, Exception exception, string trackingType, int oldUid);
}
