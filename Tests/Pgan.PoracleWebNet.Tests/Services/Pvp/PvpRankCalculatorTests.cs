using Pgan.PoracleWebNet.Core.Models.Pvp;
using Pgan.PoracleWebNet.Core.Services.Pvp;

namespace Pgan.PoracleWebNet.Tests.Services.Pvp;

public class PvpRankCalculatorTests
{
    // Well-known Pokémon base stats (attack / defense / stamina).
    private static readonly BaseStats Azumarill = new(112, 152, 225);
    private static readonly BaseStats Registeel = new(143, 285, 190);
    private static readonly BaseStats Mewtwo = new(300, 182, 214);

    [Theory]
    [InlineData(PvpLeague.Little, 500)]
    [InlineData(PvpLeague.Great, 1500)]
    [InlineData(PvpLeague.Ultra, 2500)]
    public void RankAllCombosUnderCap(PvpLeague league, int cap)
    {
        var ranked = PvpRankCalculator.Rank(Azumarill, league);

        Assert.Equal(4096, ranked.Length);
        Assert.All(ranked, combo => Assert.True(combo.Cp <= cap, $"combo {combo.Attack}/{combo.Defense}/{combo.Stamina} L{combo.Level} CP{combo.Cp} exceeds {cap}"));
    }

    [Fact]
    public void RankReturns4096Combos()
    {
        var ranked = PvpRankCalculator.Rank(Azumarill, PvpLeague.Great);
        Assert.Equal(16 * 16 * 16, ranked.Length);
    }

    [Fact]
    public void RankRanksAreSortedDescendingByStatProduct()
    {
        var ranked = PvpRankCalculator.Rank(Registeel, PvpLeague.Great);
        for (var i = 1; i < ranked.Length; i++)
        {
            Assert.True(ranked[i - 1].StatProduct >= ranked[i].StatProduct, $"stat product not monotonic at index {i}");
        }
    }

    [Fact]
    public void RankRankOneHasHighestStatProduct()
    {
        var ranked = PvpRankCalculator.Rank(Azumarill, PvpLeague.Great);
        var topProduct = ranked.Max(r => r.StatProduct);
        Assert.Equal(1, ranked[0].Rank);
        Assert.Equal(topProduct, ranked[0].StatProduct);
    }

    [Fact]
    public void RankPercentageIsRelativeToRankOne()
    {
        var ranked = PvpRankCalculator.Rank(Azumarill, PvpLeague.Great);
        Assert.Equal(1.0, ranked[0].Percentage, 6);
        Assert.All(ranked, combo => Assert.InRange(combo.Percentage, 0.0, 1.0));
    }

    [Fact]
    public void RankRanksAreMonotonicAndStartAtOne()
    {
        var ranked = PvpRankCalculator.Rank(Registeel, PvpLeague.Ultra);
        Assert.Equal(1, ranked[0].Rank);
        for (var i = 1; i < ranked.Length; i++)
        {
            Assert.True(ranked[i].Rank >= ranked[i - 1].Rank, $"rank regressed at {i}");
        }
    }

    [Fact]
    public void RankMasterLeagueAllCombosAtMaxLevel()
    {
        var ranked = PvpRankCalculator.Rank(Mewtwo, PvpLeague.Master);
        Assert.All(ranked, combo => Assert.Equal(CpMultiplierTable.MaxLevel, combo.Level));
    }

    [Fact]
    public void RankMasterLeagueRankOneIsTripleFifteen()
    {
        var ranked = PvpRankCalculator.Rank(Mewtwo, PvpLeague.Master);
        var rankOne = ranked[0];
        Assert.Equal(15, rankOne.Attack);
        Assert.Equal(15, rankOne.Defense);
        Assert.Equal(15, rankOne.Stamina);
    }

    [Fact]
    public void RankGreatLeagueAzumarillRankOneFavorsBulk()
    {
        // Azumarill is a well-known "bulk over attack" GL pick. The top-ranked combo should
        // have defense and stamina maxed (or near-maxed) with a low attack IV.
        var ranked = PvpRankCalculator.Rank(Azumarill, PvpLeague.Great);
        var rankOne = ranked[0];
        Assert.True(rankOne.Defense >= 14, $"expected bulky defense, got {rankOne.Defense}");
        Assert.True(rankOne.Stamina >= 14, $"expected bulky stamina, got {rankOne.Stamina}");
        Assert.True(rankOne.Attack <= 5, $"expected low attack, got {rankOne.Attack}");
        Assert.True(rankOne.Cp <= 1500, $"CP {rankOne.Cp} exceeds cap");
        Assert.True(rankOne.Cp >= 1480, $"CP {rankOne.Cp} unusually low for rank 1");
    }

    [Fact]
    public void RankGreatLeagueRegisteelRankOneHitsCap()
    {
        // Registeel under GL cap sits near the top — rank 1 should push CP into the 1490–1500 band.
        var ranked = PvpRankCalculator.Rank(Registeel, PvpLeague.Great);
        var rankOne = ranked[0];
        Assert.InRange(rankOne.Cp, 1480, 1500);
    }

    [Fact]
    public void FirstInRankRangeReturnsLowestMatchingRank()
    {
        var ranked = PvpRankCalculator.Rank(Azumarill, PvpLeague.Great);
        var top = PvpRankCalculator.FirstInRankRange(ranked, 1, 1);
        Assert.NotNull(top);
        Assert.Equal(1, top.Value.Rank);

        var mid = PvpRankCalculator.FirstInRankRange(ranked, 50, 100);
        Assert.NotNull(mid);
        Assert.InRange(mid.Value.Rank, 50, 100);
    }

    [Fact]
    public void FirstInRankRangeSwappedBoundsStillMatches()
    {
        var ranked = PvpRankCalculator.Rank(Azumarill, PvpLeague.Great);
        var swapped = PvpRankCalculator.FirstInRankRange(ranked, 10, 1);
        Assert.NotNull(swapped);
        Assert.InRange(swapped.Value.Rank, 1, 10);
    }

    [Fact]
    public void FirstInRankRangeNoMatchReturnsNull()
    {
        var ranked = PvpRankCalculator.Rank(Azumarill, PvpLeague.Great);
        var none = PvpRankCalculator.FirstInRankRange(ranked, 99999, 100000);
        Assert.Null(none);
    }

    [Fact]
    public void CpMultiplierTableHasExpectedSize() =>
        // 109 entries covers levels 1.0 → 55.0 in 0.5 steps.
        Assert.Equal(109, CpMultiplierTable.Values.Length);

    [Fact]
    public void CpMultiplierTableIndexForLevelRoundTrip()
    {
        for (var level = CpMultiplierTable.MinLevel; level <= CpMultiplierTable.MaxLevel; level += CpMultiplierTable.LevelStep)
        {
            var idx = CpMultiplierTable.IndexForLevel(level);
            Assert.Equal(level, CpMultiplierTable.LevelForIndex(idx));
        }
    }
}

public class PvpRankServiceTests
{
    [Fact]
    public void GetRankTableCachesAcrossCalls()
    {
        var cache = new Microsoft.Extensions.Caching.Memory.MemoryCache(
            new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
        var service = new PvpRankService(cache);
        var stats = new BaseStats(112, 152, 225);

        var first = service.GetRankTable(184, 0, stats, PvpLeague.Great);
        var second = service.GetRankTable(184, 0, stats, PvpLeague.Great);

        Assert.Same(first, second);
    }

    [Fact]
    public void ResolveRankReturnsComboInRange()
    {
        var cache = new Microsoft.Extensions.Caching.Memory.MemoryCache(
            new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
        var service = new PvpRankService(cache);
        var stats = new BaseStats(112, 152, 225);

        var combo = service.ResolveRank(184, 0, stats, PvpLeague.Great, 1, 5);

        Assert.NotNull(combo);
        Assert.InRange(combo.Value.Rank, 1, 5);
    }
}
