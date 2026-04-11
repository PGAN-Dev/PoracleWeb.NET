namespace Pgan.PoracleWebNet.Core.Abstractions.Repositories;

/// <summary>
/// Atomic writer for user area mutations that affect both <c>humans.area</c> and
/// <c>profiles.area</c>. Every method commits both rows in a single <c>SaveChangesAsync</c>
/// call, so the two writes happen inside a single EF Core implicit transaction and cannot drift.
/// </summary>
/// <remarks>
/// HACK: trusted-set-areas (see docs/poracleng-enhancement-requests.md)
/// This abstraction exists because PoracleNG's <c>POST /api/humans/{id}/setAreas</c> silently
/// strips fences with <c>userSelectable=false</c> — which includes every user-drawn custom
/// geofence served from PoracleWeb's feed. Until PoracleNG ships a trusted setAreas variant,
/// user geofence activation/deactivation must be written directly to the Poracle DB, and those
/// writes must span both <c>humans.area</c> (the active-profile working copy) and the current
/// <c>profiles.area</c> row (the per-profile authoritative storage) atomically.
/// </remarks>
public interface IUserAreaDualWriter
{
    /// <summary>
    /// Adds <paramref name="areaName"/> to <c>humans.area</c> and the current <c>profiles.area</c>
    /// row for <paramref name="humanId"/>. Idempotent — no-op if the name is already present in both.
    /// Both writes are committed in a single <c>SaveChangesAsync</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The human row does not exist.</exception>
    /// <returns><c>true</c> if at least one row was actually modified.</returns>
    public Task<bool> AddAreaToActiveProfileAsync(string humanId, string areaName);

    /// <summary>
    /// Removes <paramref name="areaName"/> from <c>humans.area</c> and the current
    /// <c>profiles.area</c> row for <paramref name="humanId"/>. Idempotent — no-op if the name
    /// is not present in either. Both writes are committed in a single <c>SaveChangesAsync</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The human row does not exist.</exception>
    /// <returns><c>true</c> if at least one row was actually modified.</returns>
    public Task<bool> RemoveAreaFromActiveProfileAsync(string humanId, string areaName);

    /// <summary>
    /// Adds every name in <paramref name="areaNames"/> to <c>humans.area</c> and the current
    /// <c>profiles.area</c> row for <paramref name="humanId"/>. All additions (across the entire
    /// list and both rows) are committed in a single <c>SaveChangesAsync</c>. Used by
    /// <c>AreaController.UpdateAreas</c>'s merge-back step so saving the Areas page costs one DB
    /// round-trip regardless of how many custom geofences the user owns.
    /// </summary>
    /// <exception cref="InvalidOperationException">The human row does not exist.</exception>
    /// <returns><c>true</c> if at least one row was actually modified.</returns>
    public Task<bool> AddAreasToActiveProfileAsync(string humanId, IReadOnlyCollection<string> areaNames);

    /// <summary>
    /// Removes <paramref name="areaName"/> from <c>humans.area</c> and every row in
    /// <c>profiles.area</c> for <paramref name="humanId"/>. All removals are committed in a
    /// single <c>SaveChangesAsync</c>. Used by geofence delete so the stale name is wiped
    /// out of every profile, not just the active one. No exception is raised when the human is
    /// missing — deletion should be permissive.
    /// </summary>
    /// <returns><c>true</c> if at least one row was actually modified.</returns>
    public Task<bool> RemoveAreaFromAllProfilesAsync(string humanId, string areaName);
}
