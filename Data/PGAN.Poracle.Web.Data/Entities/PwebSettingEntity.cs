using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PGAN.Poracle.Web.Data.Entities;

[Table("pweb_settings")]
public class PwebSettingEntity
{
    [Key]
    [Column("setting")]
    public string Setting { get; set; } = null!;

    [Column("value")]
    public string? Value { get; set; }
}
