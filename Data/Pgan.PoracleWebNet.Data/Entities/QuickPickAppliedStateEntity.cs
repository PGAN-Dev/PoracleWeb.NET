using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Pgan.PoracleWebNet.Data.Entities;

[Table("quick_pick_applied_states")]
public class QuickPickAppliedStateEntity
{
    [Key]
    [Column("id")]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id
    {
        get; set;
    }

    [Column("user_id")]
    [Required]
    public string UserId { get; set; } = string.Empty;

    [Column("profile_no")]
    public int ProfileNo
    {
        get; set;
    }

    [Column("quick_pick_id")]
    [Required]
    public string QuickPickId { get; set; } = string.Empty;

    [Column("alarm_type")]
    [Required]
    public string AlarmType { get; set; } = "monster";

    [Column("applied_at")]
    public DateTime AppliedAt { get; set; } = DateTime.UtcNow;

    [Column("exclude_pokemon_ids_json")]
    public string? ExcludePokemonIdsJson
    {
        get; set;
    }

    [Column("tracked_uids_json")]
    [Required]
    public string TrackedUidsJson { get; set; } = "[]";
}
