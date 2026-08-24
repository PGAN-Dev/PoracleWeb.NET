using System.Text.Json;
using Pgan.PoracleWebNet.Core.Abstractions.Services;

namespace Pgan.PoracleWebNet.Core.Services;

/// <summary>
/// The v2 branch every alarm service takes before its v1 update path.
/// </summary>
/// <remarks>
/// <para>
/// <c>PUT /api/v2/humans/{id}/tracking/{type}/{uid}</c> is a genuine full replace addressed by uid, so it
/// needs none of the machinery the v1 create-carrying-a-uid does: no reconcile of a stray insert, no
/// delete-first to free a natural key, no restore-on-failure. Skipping those as a unit is the point —
/// running any of them against a write that already landed would be worse than not moving at all.
/// </para>
/// <para>
/// The uid rotates, because v2's engine is delete-then-insert. Quick-pick applied state stores uids
/// captured at apply time and has to follow the row, or its "remove" button silently deletes nothing.
/// See #403.
/// </para>
/// </remarks>
internal static class TrackingV2Replacement
{
    /// <summary>
    /// Replaces the rule through <c>/api/v2</c> when that surface is available for this type, this server
    /// and this row.
    /// </summary>
    /// <returns>
    /// The uid the rule now lives under, or null when the caller should take its own v1 path — the type
    /// has no v2 field table, the operator pinned v1, the route is absent, or the row carries something
    /// v2 could not be told faithfully.
    /// </returns>
    public static async Task<int?> TryApplyAsync(
        IPoracleTrackingProxy proxy,
        string trackingType,
        string userId,
        int oldUid,
        JsonElement body,
        ITrackedUidRemapper uidRemapper)
    {
        if (await proxy.TryReplaceV2Async(trackingType, userId, oldUid, body) is not { } replaced)
        {
            return null;
        }

        var newUid = replaced.Uid > 0 ? replaced.Uid : oldUid;

        if (newUid != oldUid)
        {
            await uidRemapper.RemapAsync(userId, trackingType, oldUid, newUid);
        }

        return newUid;
    }
}
