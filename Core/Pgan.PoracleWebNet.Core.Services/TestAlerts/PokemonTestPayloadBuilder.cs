using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models.Pvp;
using Pgan.PoracleWebNet.Core.Services.Pvp;

namespace Pgan.PoracleWebNet.Core.Services.TestAlerts;

/// <summary>
/// Builds a monster webhook that honors the alarm's IV, level, CP, size, gender and PVP filters.
/// When the filter specifies a PVP league + rank range, the IV combo is resolved against the live
/// PVP rank calculator so the DM shows real rank-1 values (e.g. Great League rank 1 Tinkaton at
/// 13/15/15 L49.5) instead of placeholder data.
/// </summary>
public sealed class PokemonTestPayloadBuilder(
    IPvpRankService pvpRankService,
    IMasterDataService masterDataService) : ITestPayloadBuilder
{
    // Default species for "any pokemon" filters, used only as a last resort visual.
    private const int DefaultPokemonId = 25; // Pikachu

    public bool CanBuild(string alarmType) => alarmType == "pokemon";

    public async Task<TestPayloadBuildResult> BuildAsync(TestPayloadContext context)
    {
        var alarm = context.Alarm;
        var pokemonId = alarm.GetInt("pokemon_id", DefaultPokemonId);
        if (pokemonId <= 0)
        {
            pokemonId = DefaultPokemonId;
        }

        var form = alarm.GetInt("form", 0);
        var baseStats = await masterDataService.GetBaseStatsAsync(pokemonId, form);

        // Filter bounds.
        var minLevel = Math.Clamp(alarm.GetInt("min_level", 1), 1, 55);
        var maxLevel = Math.Clamp(alarm.GetInt("max_level", 50), minLevel, 55);
        var minIvPct = alarm.GetInt("min_iv", 0);
        var maxIvPct = alarm.GetInt("max_iv", 100);
        var atkFloor = Math.Clamp(alarm.GetInt("atk", 0), 0, 15);
        var defFloor = Math.Clamp(alarm.GetInt("def", 0), 0, 15);
        var staFloor = Math.Clamp(alarm.GetInt("sta", 0), 0, 15);
        var atkCeil = Math.Clamp(alarm.GetInt("max_atk", 15), atkFloor, 15);
        var defCeil = Math.Clamp(alarm.GetInt("max_def", 15), defFloor, 15);
        var staCeil = Math.Clamp(alarm.GetInt("max_sta", 15), staFloor, 15);
        var minCp = alarm.GetInt("min_cp", 0);
        var maxCp = alarm.GetInt("max_cp", int.MaxValue);

        var pvpLeague = alarm.GetInt("pvp_ranking_league", 0);
        var pvpBest = alarm.GetInt("pvp_ranking_best", 0);
        var pvpWorst = alarm.GetInt("pvp_ranking_worst", 4096);

        int atkIv;
        int defIv;
        int staIv;
        double level;
        int cp;
        var greatLeagueRanks = new List<Dictionary<string, object>>();
        var ultraLeagueRanks = new List<Dictionary<string, object>>();

        // PVP filter path — resolve IVs/level/CP from the rank ranger.
        if (pvpLeague > 0 && pvpBest > 0 && baseStats is { } stats)
        {
            var league = MapLeague(pvpLeague);
            var combo = pvpRankService.ResolveRank(pokemonId, form, stats, league, pvpBest, pvpWorst);
            if (combo is { } c)
            {
                atkIv = c.Attack;
                defIv = c.Defense;
                staIv = c.Stamina;
                level = c.Level;
                cp = c.Cp;
            }
            else
            {
                (atkIv, defIv, staIv, level, cp) = FallbackStats(stats, atkFloor, defFloor, staFloor, atkCeil, defCeil, staCeil, minLevel, maxLevel);
            }
        }
        else if (baseStats is { } s)
        {
            // Non-PVP filter path — pick IVs/level from explicit ranges, compute CP from base stats.
            (atkIv, defIv, staIv, level, cp) = FallbackStats(s, atkFloor, defFloor, staFloor, atkCeil, defCeil, staCeil, minLevel, maxLevel);
        }
        else
        {
            // No base stats — fall back to synthetic values that still honor the filter.
            atkIv = FilterFieldExtensions.PickInRange(atkFloor, atkCeil, 15);
            defIv = FilterFieldExtensions.PickInRange(defFloor, defCeil, 15);
            staIv = FilterFieldExtensions.PickInRange(staFloor, staCeil, 15);
            level = FilterFieldExtensions.PickLevelInRange(minLevel, maxLevel, 35);
            cp = Math.Clamp(1000, minCp, maxCp == 0 ? int.MaxValue : maxCp);
        }

        // Honor combined-IV percentage filter when it's strict. A small local search is
        // enough — the space is tiny (16³) and we only need ONE combo that satisfies both
        // the per-stat ranges AND the combined %-IV range. Applied BEFORE the rank panel
        // build so the rendered PVP rank is computed against the final IVs, not stale ones.
        var totalIv = atkIv + defIv + staIv;
        var ivPct = totalIv / 45.0 * 100.0;
        var filterIsSatisfiable = true;
        if (ivPct < minIvPct || ivPct > maxIvPct)
        {
            var found = FindCombinedIv(atkFloor, atkCeil, defFloor, defCeil, staFloor, staCeil, minIvPct, maxIvPct);
            if (found is { } f)
            {
                atkIv = f.Atk;
                defIv = f.Def;
                staIv = f.Sta;
                if (baseStats is { } ivStats)
                {
                    cp = PvpRankCalculator.ComputeCpForStats(ivStats, atkIv, defIv, staIv, level);
                }
            }
            else
            {
                // The user's per-stat box can't contain any combo inside the %-IV range
                // (e.g. atk≤10, def≤10, sta≤10, min_iv=90). We deliberately skip the rank
                // panel in this case — the IVs we ship won't satisfy the filter, so
                // stapling a computed rank on top would hide the inconsistency from the user.
                filterIsSatisfiable = false;
            }
        }

        // Rank panels built from the FINAL IVs so the DM body and rank panel stay consistent.
        if (baseStats is { } panelStats && filterIsSatisfiable)
        {
            AppendRankPanel(greatLeagueRanks, pvpRankService, pokemonId, form, panelStats, PvpLeague.Great, atkIv, defIv, staIv);
            AppendRankPanel(ultraLeagueRanks, pvpRankService, pokemonId, form, panelStats, PvpLeague.Ultra, atkIv, defIv, staIv);
        }

        var disappearTime = context.Now.AddMinutes(10).ToUnixTimeSeconds();
        var firstSeen = context.Now.AddSeconds(-30).ToUnixTimeSeconds();

        var webhook = new Dictionary<string, object>
        {
            ["encounter_id"] = $"test-{Guid.NewGuid():N}",
            ["spawnpoint_id"] = "test-spawnpoint",
            ["pokemon_id"] = pokemonId,
            ["display_pokemon_id"] = 0,
            ["form"] = form,
            ["costume"] = 0,
            ["latitude"] = context.Latitude,
            ["longitude"] = context.Longitude,
            ["disappear_time"] = disappearTime,
            ["disappear_time_verified"] = true,
            ["verified"] = true,
            ["seen_type"] = "encounter",
            ["first_seen"] = firstSeen,
            ["last_modified_time"] = firstSeen,
            ["cp_multiplier"] = CpmForLevel(level),
            ["pokemon_level"] = level,
            ["cp"] = cp,
            ["individual_attack"] = atkIv,
            ["individual_defense"] = defIv,
            ["individual_stamina"] = staIv,
            ["move_1"] = 1,
            ["move_2"] = 1,
            ["height"] = 1.0,
            ["weight"] = 10.0,
            ["gender"] = alarm.GetInt("gender", 0) > 0 ? alarm.GetInt("gender", 1) : 1,
            ["rarity"] = 1,
            ["weather"] = 0,
            ["shiny"] = false,
        };

        var sizeFilter = alarm.GetInt("size", -1);
        if (sizeFilter >= 0)
        {
            webhook["size"] = sizeFilter;
        }

        if (greatLeagueRanks.Count > 0)
        {
            webhook["pvp_rankings_great_league"] = greatLeagueRanks;
        }

        if (ultraLeagueRanks.Count > 0)
        {
            webhook["pvp_rankings_ultra_league"] = ultraLeagueRanks;
        }

        return new TestPayloadBuildResult("pokemon", webhook);
    }

    private static (int Atk, int Def, int Sta, double Level, int Cp) FallbackStats(
        BaseStats baseStats,
        int atkFloor, int defFloor, int staFloor,
        int atkCeil, int defCeil, int staCeil,
        int minLevel, int maxLevel)
    {
        var atkIv = FilterFieldExtensions.PickInRange(atkFloor, atkCeil, 15);
        var defIv = FilterFieldExtensions.PickInRange(defFloor, defCeil, 15);
        var staIv = FilterFieldExtensions.PickInRange(staFloor, staCeil, 15);
        var level = FilterFieldExtensions.PickLevelInRange(minLevel, Math.Min(maxLevel, 40), 35);
        var cp = PvpRankCalculator.ComputeCpForStats(baseStats, atkIv, defIv, staIv, level);
        return (atkIv, defIv, staIv, level, cp);
    }

    private static double CpmForLevel(double level)
    {
        var idx = CpMultiplierTable.IndexForLevel(level);
        return CpMultiplierTable.Values[idx];
    }

    /// <summary>
    /// Maps <c>pvp_ranking_league</c> stored as a CP cap (500 = Little, 1500 = Great,
    /// 2500 = Ultra, 10000+ = Master) to the ranker's <see cref="PvpLeague"/> enum. The
    /// frontend (<c>pokemon-edit-dialog</c>) and <c>QuickPickService</c> presets write the
    /// CP cap directly — this is verified against the database column semantics.
    /// </summary>
    private static PvpLeague MapLeague(int pvpRankingLeague) => pvpRankingLeague switch
    {
        500 => PvpLeague.Little,
        1500 => PvpLeague.Great,
        2500 => PvpLeague.Ultra,
        >= 10000 => PvpLeague.Master,
        _ => PvpLeague.Great,
    };

    /// <summary>
    /// Tiny search to find an IV combination satisfying both per-stat ranges and the
    /// combined %-IV range. Used when the initial synthesis falls outside the %-IV filter.
    /// Returns null when no combination can satisfy both constraint sets (should be rare —
    /// means the user's filter is unsatisfiable).
    /// </summary>
    private static (int Atk, int Def, int Sta)? FindCombinedIv(
        int atkFloor, int atkCeil,
        int defFloor, int defCeil,
        int staFloor, int staCeil,
        int minIvPct, int maxIvPct)
    {
        // Clamp %-IV bounds so we can compute the allowable total-IV range once.
        var lowerTotal = (int)Math.Ceiling(Math.Max(0, minIvPct) / 100.0 * 45.0);
        var upperTotal = (int)Math.Floor(Math.Min(100, maxIvPct) / 100.0 * 45.0);
        if (lowerTotal > upperTotal)
        {
            return null;
        }

        // Walk from the ceiling downward — higher IVs look closer to what a user expects
        // when they filter for e.g. "80%+".
        for (var a = atkCeil; a >= atkFloor; a--)
        {
            for (var d = defCeil; d >= defFloor; d--)
            {
                for (var s = staCeil; s >= staFloor; s--)
                {
                    var total = a + d + s;
                    if (total >= lowerTotal && total <= upperTotal)
                    {
                        return (a, d, s);
                    }
                }
            }
        }

        return null;
    }

    private static void AppendRankPanel(
        List<Dictionary<string, object>> sink,
        IPvpRankService service,
        int pokemonId,
        int form,
        BaseStats baseStats,
        PvpLeague league,
        int atkIv,
        int defIv,
        int staIv)
    {
        var table = service.GetRankTable(pokemonId, form, baseStats, league);
        var match = table.FirstOrDefault(r => r.Attack == atkIv && r.Defense == defIv && r.Stamina == staIv);
        if (match.Cp == 0)
        {
            return;
        }

        sink.Add(new Dictionary<string, object>
        {
            ["pokemon"] = pokemonId,
            ["form"] = form,
            ["rank"] = match.Rank,
            ["cp"] = match.Cp,
            ["level"] = match.Level,
            ["percentage"] = match.Percentage,
        });
    }
}
