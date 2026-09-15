namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>
/// The quest <c>reward_type</c> values PoracleNG accepts, as game-master proto ids.
/// </summary>
/// <remarks>
/// These mirror PoracleNG's <c>validRewardTypes</c> allowlist. Everything here except
/// <see cref="Pokecoins"/> is accepted by every supported PoracleNG; pokecoins arrived in 5.2.0 and an
/// older server answers 400 "Unrecognised reward_type value" -- verified against 5.1.0 and 5.2.1 on the
/// dev host, which is why creating one is gated rather than simply offered.
/// </remarks>
public static class QuestRewardTypes
{
    public const int Item = 2;

    /// <summary>Stardust. The minimum amount travels in <c>reward</c>, not <c>amount</c>.</summary>
    public const int Stardust = 3;

    public const int Candy = 4;

    public const int Pokemon = 7;

    /// <summary>
    /// Pokecoins. Like stardust, the minimum amount travels in <c>reward</c>. Requires PoracleNG 5.2.0.
    /// </summary>
    public const int Pokecoins = 8;

    public const int MegaEnergy = 12;
}
