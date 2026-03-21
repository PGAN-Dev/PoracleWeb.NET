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

    [Column("koji_name")]
    [Required]
    public string KojiName { get; set; } = string.Empty;

    [Column("display_name")]
    [Required]
    public string DisplayName { get; set; } = string.Empty;

    [Column("group_name")]
    public string GroupName { get; set; } = string.Empty;

    [Column("parent_id")]
    public int ParentId { get; set; }

    [Column("status")]
    [Required]
    public string Status { get; set; } = "active";

    [Column("submitted_at")]
    public DateTime? SubmittedAt { get; set; }

    [Column("reviewed_by")]
    public string? ReviewedBy { get; set; }

    [Column("reviewed_at")]
    public DateTime? ReviewedAt { get; set; }

    [Column("review_notes")]
    public string? ReviewNotes { get; set; }

    [Column("promoted_name")]
    public string? PromotedName { get; set; }

    [Column("discord_thread_id")]
    public string? DiscordThreadId { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
