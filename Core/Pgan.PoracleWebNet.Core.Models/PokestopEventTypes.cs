namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>
/// The pokestop events PoracleNG can alert on, and the discriminator that tells an event row apart
/// from an invasion row in the table they share.
/// </summary>
/// <remarks>
/// <para>
/// A mirror of <c>pokestopEvent</c> in PoracleNG's <c>resources/data/util.json</c> (7 Gold-Stop,
/// 8 Kecleon, 9 Showcase). The wire calls the id <c>display_type</c>; the row stores the lowercased
/// name in <c>grunt_type</c>, which is how PoracleNG's own <c>isEventGruntType</c> partitions the
/// shared <c>invasion</c> table between the <c>invasion</c> and <c>incident</c> endpoints.
/// </para>
/// <para>
/// <strong>This table is not a validation allowlist and must not become one.</strong> A tenth event
/// added upstream should be refused by PoracleNG's own 422, not by us — otherwise a new event becomes
/// un-creatable here rather than merely un-pretty. What the table is for is the two things PoracleWeb
/// cannot ask upstream: which stored <c>grunt_type</c> values belong to the other endpoint, and what
/// to call an event in the UI. When upstream grows one, add it here and in the frontend twin
/// <c>shared/utils/pokestop-events.ts</c>; <c>PokestopEventTypesTests</c> holds the two in step.
/// </para>
/// </remarks>
public static class PokestopEventTypes
{
    /// <summary>A pokestop that has turned gold.</summary>
    public const int GoldStop = 7;

    /// <summary>A Kecleon hiding on a pokestop.</summary>
    public const int Kecleon = 8;

    /// <summary>A Showcase running at a pokestop.</summary>
    public const int Showcase = 9;

    /// <summary>display_type to the lowercased event name stored in <c>grunt_type</c>.</summary>
    public static IReadOnlyDictionary<int, string> ById
    {
        get;
    } = new Dictionary<int, string>
    {
        [GoldStop] = "gold-stop",
        [Kecleon] = "kecleon",
        [Showcase] = "showcase",
    };

    /// <summary>The stored <c>grunt_type</c> for a display_type, or null when it is not a known event.</summary>
    public static string? NameFor(int displayType) =>
        ById.TryGetValue(displayType, out var name) ? name : null;

    /// <summary>The display_type for a stored <c>grunt_type</c>, or null when it is not an event name.</summary>
    public static int? DisplayTypeFor(string? gruntType)
    {
        if (string.IsNullOrWhiteSpace(gruntType))
        {
            return null;
        }

        foreach (var (id, name) in ById)
        {
            if (string.Equals(name, gruntType, StringComparison.OrdinalIgnoreCase))
            {
                return id;
            }
        }

        return null;
    }

    /// <summary>
    /// Whether a stored <c>grunt_type</c> names a pokestop event rather than an invasion.
    /// </summary>
    /// <remarks>
    /// Case-insensitive on both sides, matching PoracleNG. The partition inherits upstream's
    /// assumption that pokemon-type names, the catch-alls <c>everything</c>/<c>boss</c> and event
    /// names are disjoint sets.
    /// </remarks>
    public static bool IsEventName(string? gruntType) => DisplayTypeFor(gruntType) is not null;
}
