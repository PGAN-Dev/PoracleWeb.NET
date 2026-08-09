namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>
/// Single source of truth for the <c>disable_*</c> site-setting keys consumed by
/// <c>RequireFeatureEnabledAttribute</c>, <c>TestAlertController</c>, the alarm services,
/// and (mirrored) the Angular nav. Centralized to avoid the typo class of #236 — a
/// disable-key string changed in one place but not another reproduces the original UI/API
/// mismatch.
///
/// When adding a new key here:
///   1. Add the matching admin-settings entry in
///      <c>ClientApp/src/app/modules/admin/admin-settings.component.ts</c>.
///   2. Add the same key (lowercase) to the nav definitions in
///      <c>ClientApp/src/app/app.ts</c>.
///   3. Add it to <c>SettingsMigrationService.BooleanKeys</c> and <c>CategoryMap</c>.
/// </summary>
public static class DisableFeatureKeys
{
    public const string Pokemon = "disable_mons";

    /// <summary>Eggs share the raid disable toggle since they share the raid UI in the SPA.</summary>
    public const string Raids = "disable_raids";

    public const string Quests = "disable_quests";
    public const string Invasions = "disable_invasions";
    public const string Lures = "disable_lures";
    public const string Nests = "disable_nests";
    public const string Gyms = "disable_gyms";
    public const string MaxBattles = "disable_maxbattles";
    public const string FortChanges = "disable_fort_changes";

    /// <summary>
    /// Disables the user-submitted custom-geofence feature (drawing/creating, submitting for review,
    /// and GeoJSON import). Not an alarm type — gates <c>UserGeofenceController</c> directly. Existing
    /// user geofences keep being served by the geofence feed so in-flight alerts don't break.
    /// </summary>
    public const string UserGeofences = "disable_user_geofences";

    /// <summary>
    /// Disables area (geofence subscription) management. Not an alarm type — gates
    /// <c>AreaController</c>. Existing subscriptions keep working; only changing them is blocked.
    /// </summary>
    public const string Areas = "disable_areas";

    /// <summary>
    /// Disables profile management and switching. Gates <c>ProfileController</c> and
    /// <c>ProfileOverviewController</c>. The user stays on whichever profile is currently active —
    /// <c>/api/auth/me</c> is deliberately not gated, so the JWT profile resync keeps working and
    /// PoracleNG's active-hours scheduler can still move a user between profiles.
    /// </summary>
    public const string Profiles = "disable_profiles";

    /// <summary>
    /// Disables setting a home location and its distance radius. Gates <c>LocationController</c>.
    /// A location already set stays set.
    /// </summary>
    public const string Location = "disable_location";

    /// <summary>
    /// Disables outbound geocoding - the address search and reverse lookup that call the configured
    /// Nominatim/OpenStreetMap provider. Gates the two geocode actions on <c>LocationController</c>
    /// rather than the whole controller, which is already gated by <see cref="Location"/>.
    /// </summary>
    /// <remarks>
    /// This toggle shipped in the admin UI with no consumer anywhere in the codebase, so an operator
    /// who switched it off for privacy or OSM terms-of-use reasons was still making outbound Nominatim
    /// calls. The most misleading of the four inert toggles: the others merely did nothing, while this
    /// one implied a guarantee it did not provide. See #420.
    /// </remarks>
    public const string Geocoding = "disable_nominatim";

    /// <summary>
    /// Tracking-type string (as used in PoracleNG's <c>/api/tracking/{type}</c> URLs and
    /// <c>ProfileOverviewService</c>'s alarm-type loop) → matching <c>disable_*</c> key.
    /// Lets <c>ProfileOverviewService</c>, <c>TestAlertController</c>, and any future
    /// proxy-level caller resolve the right gate without each maintaining its own copy.
    /// Note "fort" matches PoracleNG's tracking-type name; the controller route is
    /// <c>fort-changes</c> and the disable key is <c>disable_fort_changes</c> — three
    /// different spellings of the same concept, baked into the upstream API.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ByTrackingType
    {
        get;
    } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["pokemon"] = Pokemon,
        ["monster"] = Pokemon,
        ["raid"] = Raids,
        ["egg"] = Raids,
        ["quest"] = Quests,
        ["invasion"] = Invasions,
        ["lure"] = Lures,
        ["nest"] = Nests,
        ["gym"] = Gyms,
        ["maxbattle"] = MaxBattles,
        ["fort"] = FortChanges,
    };
}
