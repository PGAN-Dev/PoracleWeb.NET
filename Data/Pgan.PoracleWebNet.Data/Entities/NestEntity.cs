using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Pgan.PoracleWebNet.Data.Entities;

[Table("nests")]
public class NestEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("uid")]
    public int Uid
    {
        get; set;
    }

    [Column("id")]
    public string Id { get; set; } = null!;

    [Column("ping")]
    public string Ping { get; set; } = string.Empty;

    [Column("distance")]
    public int Distance
    {
        get; set;
    }

    [Column("pokemon_id")]
    public int PokemonId
    {
        get; set;
    }

    [Column("min_spawn_avg")]
    public int MinSpawnAvg
    {
        get; set;
    }

    [Column("form")]
    public int Form
    {
        get; set;
    }

    [Column("clean")]
    public int Clean
    {
        get; set;
    }

    [Column("template")]
    public string? Template
    {
        get; set;
    }

    [Column("profile_no")]
    public int ProfileNo { get; set; } = 1;

    [ForeignKey("Id")]
    public HumanEntity? Human
    {
        get; set;
    }
}
