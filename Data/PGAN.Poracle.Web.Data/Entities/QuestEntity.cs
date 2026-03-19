using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PGAN.Poracle.Web.Data.Entities;

[Table("quest")]
public class QuestEntity
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

    [Column("reward")]
    public int Reward
    {
        get; set;
    }

    [Column("reward_type")]
    public int RewardType
    {
        get; set;
    }

    [Column("shiny")]
    public int Shiny
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

    [Column("form")]
    public int Form
    {
        get; set;
    }

    [ForeignKey("Id")]
    public HumanEntity? Human
    {
        get; set;
    }
}
