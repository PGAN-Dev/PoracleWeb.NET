using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PGAN.Poracle.Web.Data.Entities;

[Table("gym")]
public class GymEntity
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

    [Column("team")]
    public int Team
    {
        get; set;
    }

    [Column("slot_changes")]
    public int SlotChanges
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
