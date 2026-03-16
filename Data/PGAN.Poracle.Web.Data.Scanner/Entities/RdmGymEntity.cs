using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PGAN.Poracle.Web.Data.Scanner.Entities;

[Table("gym")]
public class RdmGymEntity
{
    [Key]
    [Column("id")]
    public string Id { get; set; } = string.Empty;

    [Column("name")]
    public string? Name { get; set; }

    [Column("lat")]
    public double Lat { get; set; }

    [Column("lon")]
    public double Lon { get; set; }

    [Column("raid_level")]
    public int? RaidLevel { get; set; }

    [Column("raid_pokemon_id")]
    public int? RaidPokemonId { get; set; }

    [Column("raid_pokemon_form")]
    public int? RaidPokemonForm { get; set; }

    [Column("raid_end_timestamp")]
    public long? RaidEndTimestamp { get; set; }

    [Column("team_id")]
    public int? TeamId { get; set; }
}
