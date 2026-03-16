using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PGAN.Poracle.Web.Data.Entities;

[Table("profiles")]
public class ProfileEntity
{
    [Column("id")]
    public string Id { get; set; } = null!;

    [Column("profile_no")]
    public int ProfileNo { get; set; }

    [Column("name")]
    public string? Name { get; set; }

    [Column("area")]
    public string? Area { get; set; }

    [Column("latitude")]
    public double Latitude { get; set; }

    [Column("longitude")]
    public double Longitude { get; set; }

    [ForeignKey("Id")]
    public HumanEntity? Human { get; set; }
}
