using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Tests.Validation;

/// <summary>
/// Per-property [Range] attributes checked each bound against the game's limits and never against its
/// partner, so a transposed pair saved clean and then matched nothing, with no error to explain the
/// silence. See #461.
/// </summary>
public class MonsterRangeValidationTests
{
    private static Monster Valid() => new()
    {
        MinIv = -1,
        MaxIv = 100,
        MinCp = 0,
        MaxCp = 9000,
        MinLevel = 0,
        MaxLevel = 55,
        MinWeight = 0,
        MaxWeight = 9000000,
        MaxAtk = 15,
        MaxDef = 15,
        MaxSta = 15,
        Size = -1,
        MaxSize = 5,
        PvpRankingBest = 0,
        PvpRankingWorst = 4096,
    };

    [Fact]
    public void TheDefaultWindowsAreSatisfiable()
    {
        Assert.Null(MonsterRangeValidator.Validate(Valid()));
    }

    [Fact]
    public void AnInvertedIvWindowIsRejected()
    {
        var monster = Valid();
        monster.MinIv = 90;
        monster.MaxIv = 10;

        Assert.Contains("minIv", MonsterRangeValidator.Validate(monster), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(nameof(Monster.MinCp), 9000, nameof(Monster.MaxCp), 10)]
    [InlineData(nameof(Monster.MinLevel), 40, nameof(Monster.MaxLevel), 5)]
    [InlineData(nameof(Monster.MinWeight), 500, nameof(Monster.MaxWeight), 10)]
    [InlineData(nameof(Monster.Atk), 15, nameof(Monster.MaxAtk), 2)]
    [InlineData(nameof(Monster.Size), 5, nameof(Monster.MaxSize), 1)]
    public void EveryPairIsChecked(string minName, int min, string maxName, int max)
    {
        var monster = Valid();
        typeof(Monster).GetProperty(minName)!.SetValue(monster, min);
        typeof(Monster).GetProperty(maxName)!.SetValue(monster, max);

        Assert.NotNull(MonsterRangeValidator.Validate(monster));
    }

    /// <summary>
    /// PVP rankings count upward from the best, so "best" is the lower bound — reversing that check would
    /// reject every ordinary alarm.
    /// </summary>
    [Fact]
    public void PvpRankingIsCheckedInRankOrder()
    {
        var monster = Valid();
        monster.PvpRankingBest = 1;
        monster.PvpRankingWorst = 100;
        Assert.Null(MonsterRangeValidator.Validate(monster));

        monster.PvpRankingBest = 4000;
        monster.PvpRankingWorst = 1;
        Assert.NotNull(MonsterRangeValidator.Validate(monster));
    }

    /// <summary>
    /// minIv -1 with maxIv -1 tracks unencountered Pokemon only. It is a real filter, and it satisfies
    /// min &lt;= max, so the guard must leave it alone.
    /// </summary>
    [Fact]
    public void TheUnencounteredOnlyFilterSurvives()
    {
        var monster = Valid();
        monster.MinIv = -1;
        monster.MaxIv = -1;

        Assert.Null(MonsterRangeValidator.Validate(monster));
    }

    [Fact]
    public void EqualBoundsAreSatisfiable()
    {
        var monster = Valid();
        monster.MinIv = 100;
        monster.MaxIv = 100;

        Assert.Null(MonsterRangeValidator.Validate(monster));
    }
}
