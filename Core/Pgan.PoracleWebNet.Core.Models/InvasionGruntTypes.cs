namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>
/// The Team Rocket <c>grunt_type</c> values PoracleNG accepts for invasion tracking.
/// </summary>
/// <remarks>
/// <para>
/// PoracleNG has no catch-all: <c>grunt_type</c> must be a non-empty string, and an empty one is
/// rejected with <c>400 {"message":"Grunt type mandatory"}</c>. "Track everything" therefore has to be
/// a fan-out over this list — one row per type — which is also how the invasion table's natural key
/// (<c>id, profile_no, gender, grunt_type</c>) expects rows to be shaped. See #416.
/// </para>
/// <para>
/// This is the twin of <c>GRUNT_TYPES</c> in
/// <c>ClientApp/src/app/modules/invasions/invasion-add-dialog.component.ts</c>. The two must stay in
/// sync; <c>InvasionGruntTypesTests</c> pins the contents so a one-sided edit fails the build.
/// The frontend list additionally splits <c>mixed</c> into male and female rows for display — the
/// same <c>grunt_type</c>, differing only by gender — so it has one more entry than this one.
/// </para>
/// <para>
/// Pokestop event types (<c>kecleon</c>, <c>gold-stop</c>, <c>showcase</c>) are deliberately absent.
/// They are not Team Rocket invasions and are offered separately in the UI.
/// </para>
/// </remarks>
public static class InvasionGruntTypes
{
    /// <summary>The eighteen elemental grunt types.</summary>
    public static IReadOnlyList<string> Elemental { get; } =
    [
        "bug", "dark", "dragon", "electric", "fairy", "fighting", "fire", "flying", "ghost",
        "grass", "ground", "ice", "metal", "normal", "poison", "psychic", "rock", "water"
    ];

    /// <summary>Grunts that are not tied to a single element.</summary>
    public static IReadOnlyList<string> Special { get; } = ["mixed", "darkness", "decoy"];

    /// <summary>The three Rocket leaders. Giovanni is separate — he needs a Super Rocket Radar.</summary>
    public static IReadOnlyList<string> Leaders { get; } = ["cliff", "arlo", "sierra"];

    public const string Giovanni = "giovanni";

    /// <summary>Every Team Rocket grunt type: elemental, special, leaders and Giovanni.</summary>
    public static IReadOnlyList<string> All { get; } =
        [.. Elemental, .. Special, .. Leaders, Giovanni];
}
