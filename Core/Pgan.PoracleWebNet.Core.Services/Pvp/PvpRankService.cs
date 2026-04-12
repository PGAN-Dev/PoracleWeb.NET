using Microsoft.Extensions.Caching.Memory;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models.Pvp;

namespace Pgan.PoracleWebNet.Core.Services.Pvp;

/// <summary>
/// Caches rank tables per <c>(pokemonId, form, league)</c>. Game master changes rarely,
/// so entries have no TTL — they live for the app lifetime. ~16 KB per cached species.
/// </summary>
public sealed class PvpRankService(IMemoryCache memoryCache) : IPvpRankService
{
    private readonly IMemoryCache _memoryCache = memoryCache;

    public RankedCombo[] GetRankTable(int pokemonId, int form, BaseStats baseStats, PvpLeague league)
    {
        // Base stats are baked into the key so a game master refresh with updated stats
        // automatically misses the old entry instead of returning a stale ranking table.
        var key = $"pvp_rank::{pokemonId}::{form}::{(int)league}::{baseStats.Attack}_{baseStats.Defense}_{baseStats.Stamina}";
        return this._memoryCache.GetOrCreate(key, entry =>
        {
            entry.Priority = CacheItemPriority.NeverRemove;
            return PvpRankCalculator.Rank(baseStats, league);
        }) ?? [];
    }

    public RankedCombo? ResolveRank(int pokemonId, int form, BaseStats baseStats, PvpLeague league, int minRank, int maxRank)
    {
        var table = this.GetRankTable(pokemonId, form, baseStats, league);
        return PvpRankCalculator.FirstInRankRange(table, minRank, maxRank);
    }
}
