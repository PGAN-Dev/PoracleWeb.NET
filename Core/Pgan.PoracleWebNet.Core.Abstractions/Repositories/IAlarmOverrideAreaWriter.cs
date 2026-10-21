namespace Pgan.PoracleWebNet.Core.Abstractions.Repositories;

/// <summary>
/// The one remaining direct write to the Poracle database: an alarm's <c>override_areas</c> column.
/// Every area-subscription write goes through PoracleNG's v2 routes (<c>IUserAreaWriter</c>); this
/// stays until sub-project 3 moves alarm overrides.
/// </summary>
/// <remarks>
/// HACK: trusted-set-areas (see docs/poracleng-enhancement-requests.md)
/// PoracleNG's tracking write refuses a user-drawn geofence in <c>override_areas</c>, so the column
/// is written directly.
/// </remarks>
public interface IAlarmOverrideAreaWriter
{
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
