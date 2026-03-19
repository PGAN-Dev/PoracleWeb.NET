using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PGAN.Poracle.Web.Data.Entities;

[Table("humans")]
public class HumanEntity
{
    [Key]
    [Column("id")]
    public string Id { get; set; } = null!;

    [Column("type")]
    public string Type { get; set; } = null!;

    [Column("name")]
    public string Name { get; set; } = null!;

    [Column("enabled")]
    public int Enabled { get; set; } = 1;

    [Column("area")]
    public string Area { get; set; } = null!;

    [Column("latitude")]
    public double Latitude
    {
        get; set;
    }

    [Column("longitude")]
    public double Longitude
    {
        get; set;
    }

    [Column("fails")]
    public int Fails
    {
        get; set;
    }

    [Column("last_checked")]
    public DateTime LastChecked
    {
        get; set;
    }

    [Column("language")]
    public string? Language
    {
        get; set;
    }

    [Column("admin_disable")]
    public int AdminDisable
    {
        get; set;
    }

    [Column("disabled_date")]
    public DateTime? DisabledDate
    {
        get; set;
    }

    [Column("current_profile_no")]
    public int CurrentProfileNo { get; set; } = 1;

    [Column("community_membership")]
    public string CommunityMembership { get; set; } = null!;

    [Column("area_restriction")]
    public string? AreaRestriction
    {
        get; set;
    }

    [Column("notes")]
    public string Notes { get; set; } = string.Empty;

    [Column("blocked_alerts")]
    public string? BlockedAlerts
    {
        get; set;
    }

    public ICollection<ProfileEntity> Profiles { get; set; } = [];
    public ICollection<MonsterEntity> Monsters { get; set; } = [];
    public ICollection<RaidEntity> Raids { get; set; } = [];
    public ICollection<EggEntity> Eggs { get; set; } = [];
    public ICollection<QuestEntity> Quests { get; set; } = [];
    public ICollection<InvasionEntity> Invasions { get; set; } = [];
    public ICollection<LureEntity> Lures { get; set; } = [];
    public ICollection<NestEntity> Nests { get; set; } = [];
    public ICollection<GymEntity> Gyms { get; set; } = [];
}
