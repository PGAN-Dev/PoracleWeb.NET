namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>
/// Cross-field checks for a Pokemon alarm's min/max pairs.
/// </summary>
/// <remarks>
/// The models carry per-property <c>[Range]</c> attributes only, so each bound was checked against the
/// game's limits and never against its partner. PoracleNG's matcher ANDs every range as a sequential gate,
/// so a window like minIv 90 / maxIv 10 can never match anything: the alarm saves clean and then goes
/// silent, with no error to explain it. A transposed pair in the edit dialog was enough. Validation has to
/// run on the merged alarm rather than the request, because a PUT carrying only <c>minIv</c> inverts the
/// window against the value already stored. Same shape as the distance guard added in #417. See #461.
/// </remarks>
public static class MonsterRangeValidator
{
    /// <summary>
    /// Returns a message naming the first inverted pair, or <c>null</c> when every window is satisfiable.
    /// </summary>
    public static string? Validate(Monster monster)
    {
        ArgumentNullException.ThrowIfNull(monster);

        // PVP rankings count upward from the best, so "best" is the LOWER bound of the pair.
        return Check(monster.MinIv, monster.MaxIv, "minIv", "maxIv")
            ?? Check(monster.MinCp, monster.MaxCp, "minCp", "maxCp")
            ?? Check(monster.MinLevel, monster.MaxLevel, "minLevel", "maxLevel")
            ?? Check(monster.MinWeight, monster.MaxWeight, "minWeight", "maxWeight")
            ?? Check(monster.Atk, monster.MaxAtk, "atk", "maxAtk")
            ?? Check(monster.Def, monster.MaxDef, "def", "maxDef")
            ?? Check(monster.Sta, monster.MaxSta, "sta", "maxSta")
            ?? Check(monster.Size, monster.MaxSize, "size", "maxSize")
            ?? Check(monster.PvpRankingBest, monster.PvpRankingWorst, "pvpRankingBest", "pvpRankingWorst");
    }

    private static string? Check(int min, int max, string minName, string maxName) =>
        min > max
            ? $"{minName} ({min}) is greater than {maxName} ({max}), so no Pokemon can match this alarm."
            : null;
}
