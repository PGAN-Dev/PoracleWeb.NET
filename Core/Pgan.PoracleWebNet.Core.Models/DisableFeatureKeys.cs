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
