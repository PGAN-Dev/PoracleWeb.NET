using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PGAN.Poracle.Web.Data.Scanner.Entities;

[Table("pokestop")]
public class RdmPokestopEntity
{
    [Key]
    [Column("id")]
    public string Id { get; set; } = string.Empty;

    [Column("name")]
    public string? Name
    {
        get; set;
    }

    [Column("lat")]
    public double Lat
    {
        get; set;
    }

    [Column("lon")]
    public double Lon
    {
        get; set;
    }

    [Column("quest_type")]
    public int? QuestType
    {
        get; set;
    }

    [Column("quest_target")]
    public int? QuestTarget
    {
        get; set;
    }

    [Column("quest_reward_type")]
    public int? QuestRewardType
    {
        get; set;
    }

    [Column("quest_item_id")]
    public int? QuestItemId
    {
        get; set;
    }

    [Column("quest_pokemon_id")]
    public int? QuestPokemonId
    {
        get; set;
    }
}
