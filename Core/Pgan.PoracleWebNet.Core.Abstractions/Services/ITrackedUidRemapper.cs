namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

/// <summary>
/// Keeps quick-pick applied state pointing at live alarm rows when a uid rotates.
/// </summary>
/// <remarks>
/// <para>
/// PoracleNG implements a tracking edit as delete-and-insert for every type except monsters, so the row
/// comes back with a new uid. Quick-pick applied state persists the uids captured at apply time, and
/// removal deletes by those uids — so after any edit, removal deleted nothing, reported 204, and the
/// alarm kept firing. Worse, the summary read then saw zero surviving uids, concluded the user had
/// deleted the alarms by hand, and cleared the applied state, leaving no way to remove it from the UI
/// at all. See #403.
/// </para>
/// <para>
/// Alarm services call this whenever they observe a rotation, so the stored uid follows the row.
/// It is deliberately best-effort: a failure here must not fail an edit that already succeeded.
/// </para>
/// </remarks>
public interface ITrackedUidRemapper
{
    /// <summary>
    /// Rewrites <paramref name="oldUid"/> to <paramref name="newUid"/> in every applied state belonging to
    /// <paramref name="userId"/> for <paramref name="alarmType"/>. A no-op when no quick pick tracks the uid.
    /// </summary>
    public Task RemapAsync(string userId, string alarmType, int oldUid, int newUid);
}
