using Pgan.PoracleWebNet.Core.Models.Pvp;

namespace Pgan.PoracleWebNet.Core.Services.Pvp;

/// <summary>
/// Computes PVP rank tables for a species in a league, mirroring gohbem/ohbem.
/// Pure — no caching, no IO. Wrap with <see cref="PvpRankService"/> for reuse.
/// Port of https://github.com/UnownHash/gohbem/blob/main/pvp_core.go
/// </summary>
public static class PvpRankCalculator
{
    private const int MaxIv = 15;
    private const int IvCount = 16;
    private const int ComboCount = IvCount * IvCount * IvCount;

    /// <summary>
    /// Highest legal in-game Pokémon level at the time of writing (Best Buddy boost on a
    /// maxed buddy Pokémon). Used to clamp master league rankings; raise this if Niantic
    /// uncaps further.
    /// </summary>
    public const double CurrentInGameMaxLevel = 51.0;

    /// <summary>
    /// Rank every IV combination (0–15 × 0–15 × 0–15) for the given base stats and league cap.
    /// For each combo, picks the highest legal level under the cap, computes the effective stat
    /// product, sorts descending, and assigns 1-based ranks (ties share a rank).
    /// Master league (cap = <see cref="int.MaxValue"/>) uses <see cref="CpMultiplierTable.MaxLevel"/>.
    /// Returns exactly 4096 entries.
    /// </summary>
    public static RankedCombo[] Rank(BaseStats baseStats, PvpLeague league)
    {
        var cap = league.CpCap();
        var combos = new RankedCombo[ComboCount];
        var idx = 0;

        for (var a = 0; a <= MaxIv; a++)
        {
            for (var d = 0; d <= MaxIv; d++)
            {
                for (var s = 0; s <= MaxIv; s++)
                {
                    var combo = BestForCap(baseStats, a, d, s, cap);
                    combos[idx++] = combo;
                }
            }
        }

        // Sort by stat product descending, tiebreaking on attack IV (matches gohbem).
        Array.Sort(combos, static (lhs, rhs) =>
        {
            var cmp = rhs.StatProduct.CompareTo(lhs.StatProduct);
            if (cmp != 0)
            {
                return cmp;
            }

            return rhs.Attack.CompareTo(lhs.Attack);
        });

        var top = combos[0].StatProduct;
        var rank = 1;
        var prev = combos[0].StatProduct;
        for (var i = 0; i < combos.Length; i++)
        {
            var c = combos[i];
            if (c.StatProduct < prev)
            {
                rank = i + 1;
                prev = c.StatProduct;
            }

            combos[i] = c with
            {
                Rank = rank,
                Percentage = Math.Abs(top) < double.Epsilon ? 0 : c.StatProduct / top,
            };
        }

        return combos;
    }

    /// <summary>
    /// Pick the first combo in the ranked list whose rank falls in <paramref name="minRank"/>..<paramref name="maxRank"/>.
    /// Returns null when no combo matches (should not happen for sensible ranges).
    /// </summary>
    public static RankedCombo? FirstInRankRange(RankedCombo[] ranked, int minRank, int maxRank)
    {
        if (minRank > maxRank)
        {
            (minRank, maxRank) = (maxRank, minRank);
        }

        for (var i = 0; i < ranked.Length; i++)
        {
            var r = ranked[i].Rank;
            if (r >= minRank && r <= maxRank)
            {
                return ranked[i];
            }
        }

        return null;
    }

    private static RankedCombo BestForCap(BaseStats baseStats, int a, int d, int s, int cap)
    {
        var atk = baseStats.Attack + a;
        var def = baseStats.Defense + d;
        var sta = baseStats.Stamina + s;

        // Clamp the ranker to the current in-game trainer level ceiling. The CPM table
        // extends to level 55 for future-proofing, but no player can actually reach that
        // yet — Best Buddy boost tops out at level 51. Using 55 for master league would
        // produce CP values that cannot exist in-game, which looks wrong in test DMs.
        var maxIdx = Math.Min(
            CpMultiplierTable.Values.Length - 1,
            CpMultiplierTable.IndexForLevel(CurrentInGameMaxLevel));

        // Master league — no cap, use max level directly.
        if (cap == int.MaxValue)
        {
            return BuildCombo(a, d, s, maxIdx, atk, def, sta);
        }

        // Binary search for the largest level index whose CP is ≤ cap.
        var lo = 0;
        var hi = maxIdx;
        var best = -1;
        while (lo <= hi)
        {
            var mid = (lo + hi) >> 1;
            var cp = ComputeCp(atk, def, sta, CpMultiplierTable.Values[mid]);
            if (cp <= cap)
            {
                best = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        if (best < 0)
        {
            // Even at level 1 the combo exceeds the cap (rare — e.g. master-league-only
            // species in little league). Fall back to level 1 with zero stat product so it
            // sorts to the bottom without polluting the ranking.
            return new RankedCombo(0, a, d, s, CpMultiplierTable.MinLevel, 0, 0, 0);
        }

        return BuildCombo(a, d, s, best, atk, def, sta);
    }

    private static RankedCombo BuildCombo(int a, int d, int s, int levelIdx, int atk, int def, int sta)
    {
        var cpm = CpMultiplierTable.Values[levelIdx];
        var cp = ComputeCp(atk, def, sta, cpm);
        var atkEff = atk * cpm;
        var defEff = def * cpm;
        var staEff = Math.Floor(sta * cpm);
        var statProduct = atkEff * defEff * staEff;
        var level = CpMultiplierTable.LevelForIndex(levelIdx);
        return new RankedCombo(0, a, d, s, level, cp, statProduct, 0);
    }

    /// <summary>
    /// Canonical Pokémon GO CP formula: <c>floor(atk * sqrt(def) * sqrt(sta) * cpm² / 10)</c>
    /// clamped to a minimum of 10. Inputs are effective stats (base + IV), not IVs alone.
    /// Exposed so the test-alert payload builder can reuse the same math when synthesizing
    /// a non-PVP combo from a species' base stats.
    /// </summary>
    public static int ComputeCpForStats(BaseStats baseStats, int atkIv, int defIv, int staIv, double level)
    {
        var idx = CpMultiplierTable.IndexForLevel(level);
        var cpm = CpMultiplierTable.Values[idx];
        return ComputeCp(baseStats.Attack + atkIv, baseStats.Defense + defIv, baseStats.Stamina + staIv, cpm);
    }

    private static int ComputeCp(int atk, int def, int sta, double cpm)
    {
        var raw = atk * Math.Sqrt(def) * Math.Sqrt(sta) * cpm * cpm / 10.0;
        var cp = (int)Math.Floor(raw);
        return cp < 10 ? 10 : cp;
    }
}
