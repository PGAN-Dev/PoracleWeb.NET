using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PGAN.Poracle.Web.Data.Entities;

[Table("monsters")]
public class MonsterEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("uid")]
    public int Uid { get; set; }

    [Column("id")]
    public string Id { get; set; } = null!;

    [Column("pokemon_id")]
    public int PokemonId { get; set; }

    [Column("ping")]
    public string Ping { get; set; } = string.Empty;

    [Column("distance")]
    public int Distance { get; set; }

    [Column("min_iv")]
    public int MinIv { get; set; }

    [Column("max_iv")]
    public int MaxIv { get; set; } = 100;

    [Column("min_cp")]
    public int MinCp { get; set; }

    [Column("max_cp")]
    public int MaxCp { get; set; } = 9000;

    [Column("min_level")]
    public int MinLevel { get; set; }

    [Column("max_level")]
    public int MaxLevel { get; set; } = 50;

    [Column("min_weight")]
    public int MinWeight { get; set; }

    [Column("max_weight")]
    public int MaxWeight { get; set; } = 9000000;

    [Column("atk")]
    public int Atk { get; set; }

    [Column("def")]
    public int Def { get; set; }

    [Column("sta")]
    public int Sta { get; set; }

    [Column("max_atk")]
    public int MaxAtk { get; set; } = 15;

    [Column("max_def")]
    public int MaxDef { get; set; } = 15;

    [Column("max_sta")]
    public int MaxSta { get; set; } = 15;

    [Column("pvp_ranking_worst")]
    public int PvpRankingWorst { get; set; } = 4096;

    [Column("pvp_ranking_best")]
    public int PvpRankingBest { get; set; }

    [Column("pvp_ranking_min_cp")]
    public int PvpRankingMinCp { get; set; }

    [Column("pvp_ranking_league")]
    public int PvpRankingLeague { get; set; }

    [Column("form")]
    public int Form { get; set; }

    [Column("gender")]
    public int Gender { get; set; }

    [Column("clean")]
    public int Clean { get; set; }

    [Column("template")]
    public string? Template { get; set; }

    [Column("profile_no")]
    public int ProfileNo { get; set; } = 1;

    [ForeignKey("Id")]
    public HumanEntity? Human { get; set; }
}
