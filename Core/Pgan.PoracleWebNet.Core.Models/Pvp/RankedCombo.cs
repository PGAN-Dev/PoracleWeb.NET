namespace Pgan.PoracleWebNet.Core.Models.Pvp;

/// <summary>
/// One ranked IV combination for a species in a PVP league.
/// </summary>
/// <param name="Rank">1-based rank (1 = strongest). Ties share a rank.</param>
/// <param name="Attack">Attack IV 0–15.</param>
/// <param name="Defense">Defense IV 0–15.</param>
/// <param name="Stamina">Stamina IV 0–15.</param>
/// <param name="Level">Half-level step (1.0–55.0).</param>
/// <param name="Cp">Combat Power at the chosen level.</param>
/// <param name="StatProduct">Effective stat product used to rank (atk·def·⌊sta·cpm⌋).</param>
/// <param name="Percentage">StatProduct divided by the rank-1 stat product (1.0 = best).</param>
public readonly record struct RankedCombo(
    int Rank,
    int Attack,
    int Defense,
    int Stamina,
    double Level,
    int Cp,
    double StatProduct,
    double Percentage);
