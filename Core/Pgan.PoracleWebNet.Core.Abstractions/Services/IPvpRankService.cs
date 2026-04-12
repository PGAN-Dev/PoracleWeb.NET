using Pgan.PoracleWebNet.Core.Models.Pvp;

namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

public interface IPvpRankService
{
    /// <summary>
    /// Return the cached rank table (all 4096 combos) for a species+form+league.
    /// First call pays the sweep cost; subsequent calls are O(1) cache hits.
    /// </summary>
    public RankedCombo[] GetRankTable(int pokemonId, int form, BaseStats baseStats, PvpLeague league);

    /// <summary>
    /// Resolve a target rank window to a concrete IV combo for the species+form+league.
    /// Returns null when the rank range has no matching entry.
    /// </summary>
    public RankedCombo? ResolveRank(int pokemonId, int form, BaseStats baseStats, PvpLeague league, int minRank, int maxRank);
}
