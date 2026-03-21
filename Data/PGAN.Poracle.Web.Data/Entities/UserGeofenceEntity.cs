using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PGAN.Poracle.Web.Data.Entities;

[Table("user_geofences")]
public class UserGeofenceEntity
{
    [Key]
    [Column("id")]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Column("human_id")]
    [Required]
    public string HumanId { get; set; } = string.Empty;

    [Column("profile_no")]
    [Required]
    public int ProfileNo { get; set; }

    [Column("geofence_name")]
    [Required]
    public string GeofenceName { get; set; } = string.Empty;

    [Column("display_name")]
    [Required]
    public string DisplayName { get; set; } = string.Empty;

    [Column("group_name")]
    [Required]
    public string GroupName { get; set; } = string.Empty;

    [Column("parent_id")]
    [Required]
    public int ParentId { get; set; }

    [Column("polygon_json")]
    [Required]
    public string PolygonJson { get; set; } = string.Empty;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
