namespace Pgan.PoracleWebNet.Core.Models;

public class Monster
{
    public int Uid
    {
        get; set;
    }
    public string Id { get; set; } = string.Empty;
    public int PokemonId
    {
        get; set;
    }
    public string? Ping
    {
        get; set;
    }
    public int Distance
    {
        get; set;
    }
    public int MinIv
    {
        get; set;
    }
    public int MaxIv { get; set; } = 100;
    public int MinCp
    {
        get; set;
    }
    public int MaxCp { get; set; } = 9000;
    public int MinLevel
    {
        get; set;
    }
    public int MaxLevel { get; set; } = 55;
    public int MinWeight
    {
        get; set;
    }
    public int MaxWeight { get; set; } = 9000000;
    public int Atk
    {
        get; set;
    }
    public int Def
    {
        get; set;
    }
    public int Sta
    {
        get; set;
    }
    public int MaxAtk { get; set; } = 15;
    public int MaxDef { get; set; } = 15;
    public int MaxSta { get; set; } = 15;
    public int PvpRankingWorst { get; set; } = 4096;
    public int PvpRankingBest
    {
        get; set;
    }
    public int PvpRankingMinCp
    {
        get; set;
    }
    public int PvpRankingLeague
    {
        get; set;
    }
    public int PvpRankingCap
    {
        get; set;
    }

    /// <summary>
    /// Which form of the pokemon the PVP ranks are read from: 0 base, 1 any mega, 2 Mega X, 3 Mega Y.
    /// </summary>
    /// <remarks>
    /// Only consulted when a league is set. 0 means base ranks, and whether mega entries also match is
    /// then the server's <c>include_mega_evolution</c> default. PoracleNG 5.1.0.
    /// </remarks>
    public int PvpRankingEvolution
    {
        get; set;
    }
    public int Form
    {
        get; set;
    }
    /// <summary>
    /// Seconds a spawn must still have left when it is found, or the alert is skipped.
    /// </summary>
    /// <remarks>
    /// PoracleNG compares this against the spawn's time-to-hidden, so it answers "is this still worth
    /// walking to?". 0 means any.
    /// </remarks>
    public int MinTime
    {
        get; set;
    }

    public int Size { get; set; } = -1;
    public int MaxSize { get; set; } = 5;
    public int Gender
    {
        get; set;
    }
    public int Clean
    {
        get; set;
    }
    public string? Template
    {
        get; set;
    }
    public int ProfileNo
    {
        get; set;
    }

    /// <summary>
    /// Saved-place label this alarm measures its radius from, instead of the profile's pin.
    /// </summary>
    /// <remarks>
    /// Mutually exclusive with <see cref="OverrideAreas"/>, and meaningless without a distance —
    /// PoracleNG refuses both combinations. A label that no longer exists is not an error: PoracleNG
    /// falls through to the profile pin, so deleting a place widens its alarms rather than breaking them.
    /// </remarks>
    public string? OverrideLocationLabel
    {
        get; set;
    }

    /// <summary>
    /// Areas this alarm is confined to, instead of the profile's area list.
    /// </summary>
    /// <remarks>
    /// Replaces the profile's areas outright rather than intersecting with them, and is mutually
    /// exclusive with a distance. Names are lowercase with spaces, matching the geofence convention.
    /// </remarks>
    public List<string>? OverrideAreas
    {
        get; set;
    }
}
