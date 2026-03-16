namespace PGAN.Poracle.Web.Core.Models;

public class MonsterCreate
{
    public int PokemonId { get; set; }
    public string? Ping { get; set; }
    public int Distance { get; set; }
    public int MinIv { get; set; }
    public int MaxIv { get; set; }
    public int MinCp { get; set; }
    public int MaxCp { get; set; }
    public int MinLevel { get; set; }
    public int MaxLevel { get; set; }
    public int MinWeight { get; set; }
    public int MaxWeight { get; set; }
    public int Atk { get; set; }
    public int Def { get; set; }
    public int Sta { get; set; }
    public int MaxAtk { get; set; }
    public int MaxDef { get; set; }
    public int MaxSta { get; set; }
    public int PvpRankingWorst { get; set; }
    public int PvpRankingBest { get; set; }
    public int PvpRankingMinCp { get; set; }
    public int PvpRankingLeague { get; set; }
    public int Form { get; set; }
    public int Gender { get; set; }
    public int Clean { get; set; }
    public string? Template { get; set; }
    public int ProfileNo { get; set; }
}
