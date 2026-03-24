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
    public int Form
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
}
