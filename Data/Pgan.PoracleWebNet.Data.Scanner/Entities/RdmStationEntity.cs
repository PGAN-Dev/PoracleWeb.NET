using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Pgan.PoracleWebNet.Data.Scanner.Entities;

[Table("station")]
public class RdmStationEntity
{
    [Key]
    [Column("id")]
    public string Id { get; set; } = string.Empty;

    [Column("battle_pokemon_id")]
    public int? BattlePokemonId
    {
        get; set;
    }
}
