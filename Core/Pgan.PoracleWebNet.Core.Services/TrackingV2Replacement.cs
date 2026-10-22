using System.Text.Json;
using Pgan.PoracleWebNet.Core.Abstractions.Services;

namespace Pgan.PoracleWebNet.Core.Services;

/// <summary>
/// How every alarm service edits a rule.
/// </summary>
/// <remarks>
/// <para>
/// <c>PUT /api/v2/humans/{id}/tracking/{type}/{uid}</c> is a genuine full replace addressed by uid, so it
/// needs none of the machinery the v1 create-carrying-a-uid does: no reconcile of a stray insert, no
/// delete-first to free a natural key, no restore-on-failure. Skipping those as a unit is the point —
/// running any of them against a write that already landed would be worse than not moving at all.
/// </para>
/// <para>
/// On 5.2.x the uid rotates, because v2's engine is delete-then-insert. Since jfberry/PoracleNG #303 the
/// PUT keeps the rule's uid, but a failed reassignment still answers 200 with the new one, so the
/// response is read every time and the remap fires only when the uid really moved. Quick-pick applied
/// state stores uids captured at apply time and has to follow the row, or its "remove" button silently
/// deletes nothing. See #403.
/// </para>
/// </remarks>
internal static class TrackingV2Replacement
{
    /// <summary>
    /// Replaces the rule through <c>/api/v2</c>; PoracleNG answers 409 itself when the replacement would
    /// duplicate another rule.
    /// </summary>
    /// <returns>The uid the rule now lives under.</returns>
    public static async Task<int> ApplyAsync(
        IPoracleTrackingProxy proxy,
        string trackingType,
        string userId,
        int oldUid,
        JsonElement body,
        ITrackedUidRemapper uidRemapper)
    {
        var replaced = await proxy.UpdateByUidAsync(trackingType, userId, oldUid, body);

        var newUid = replaced.Uid > 0 ? replaced.Uid : oldUid;

        if (newUid != oldUid)
        {
            await uidRemapper.RemapAsync(userId, trackingType, oldUid, newUid);
        }

        return newUid;
    }
}
