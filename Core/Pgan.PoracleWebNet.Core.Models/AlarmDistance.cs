namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>
/// The bounds on an alarm's radius.
/// </summary>
public static class AlarmDistance
{
    /// <summary>
    /// Half the Earth's equatorial circumference in metres: no two points on the planet are further apart,
    /// so a larger radius cannot mean anything.
    /// </summary>
    /// <remarks>
    /// Deliberately the physically impossible rather than a product limit. Production holds a Pokemon rule
    /// at 10,000,000 m, which is someone asking for "everywhere", and it has to stay editable -- the same
    /// reasoning that keeps <c>pokemon_id</c> uncapped (a rule at 5000 exists, and new species keep
    /// arriving). A value like 99,999,999 is simply a typo, which PoracleNG accepted and stored.
    /// </remarks>
    public const int MaxMetres = 20_037_509;
}
