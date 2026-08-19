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


    /// <summary>
    /// Replaces <paramref name="oldName"/> with <paramref name="newName"/> in <c>humans.area</c> and in
    /// every row of <c>profiles.area</c> for <paramref name="humanId"/>, committed in a single
    /// <c>SaveChangesAsync</c>. Only rows that actually held the old name are touched, so per-profile
    /// activation survives the rename: a geofence active on profile 2 but not profile 3 stays that way.
    /// </summary>
    /// <remarks>
    /// Used when an admin approves a custom geofence under a different public name. Going through
    /// <c>SetAreasAsync</c> there loses the subscription entirely — the old name is
    /// <c>userSelectable=false</c> so PoracleNG strips it, and the promoted name is not yet in
    /// PoracleNG's fence list because the reload happens afterwards, so it is stripped too. See #408.
    /// </remarks>
    /// <returns><c>true</c> if at least one row was actually modified.</returns>
    public Task<bool> RenameAreaInAllProfilesAsync(string humanId, string oldName, string newName);

    /// <summary>
    /// Writes <paramref name="areaNames"/> into the <c>override_areas</c> column of one alarm row,
    /// scoped to <paramref name="humanId"/> so a caller cannot reach another user's alarms.
    /// An empty collection clears the column to NULL.
    /// </summary>
    /// <remarks>
    /// HACK: trusted-set-areas. Same root cause as the rest of this interface, one layer along.
    /// PoracleNG's tracking write validates every entry of <c>override_areas</c> against
    /// <c>GetAvailableAreas</c>, which filters on <c>userSelectable</c> for non-admins, so a user's own
    /// drawn geofence is refused outright with 400 "area not permitted" — the whole request fails, it is
    /// not silently stripped as <c>setAreas</c> does.
    /// <para>
    /// Matching, by contrast, never consults <c>userSelectable</c>: <c>resolveOverride</c> hands the
    /// rule's areas to <c>areaOverlap</c>, which compares names against the fences the spawn fell in
    /// (processor/internal/matching/generic.go). Verified at PoracleNG 5.1.0. So a name written straight
    /// into the column matches exactly like a permitted one, which is what makes this workaround correct
    /// rather than merely convenient.
    /// </para>
    /// <para>
    /// Callers must trigger <c>ReloadStateAsync</c> afterwards: PoracleNG reloads tracking state on its
    /// own mutations, and a direct write is not one of them. Without it the override waits for the
    /// periodic reload (<c>tuning.reload_interval_secs</c>, 60s by default).
    /// </para>
    /// </remarks>
    /// <param name="trackingType">PoracleNG's name for the type: pokemon, raid, egg, quest, invasion,
    /// lure, nest, gym, fort or maxbattle.</param>
    /// <exception cref="ArgumentOutOfRangeException">The tracking type is not one of the ten.</exception>
    /// <returns><c>true</c> if the row existed and was updated.</returns>
    public Task<bool> SetAlarmOverrideAreasAsync(
        string humanId, string trackingType, int uid, IReadOnlyCollection<string> areaNames);
}
