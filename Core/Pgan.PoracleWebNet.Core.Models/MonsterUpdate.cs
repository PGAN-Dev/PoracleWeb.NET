using System.ComponentModel.DataAnnotations;

namespace Pgan.PoracleWebNet.Core.Models;

public class MonsterUpdate
{
    [StringLength(256)]
    public string? Ping
    {
        get; set;
    }

    [Range(0, int.MaxValue)]
    public int? Distance
    {
        get; set;
    }

    [Range(-1, 100)]
    public int? MinIv
    {
        get; set;
    }

    [Range(-1, 100)]
    public int? MaxIv
    {
        get; set;
    }

    [Range(0, 10000)]
    public int? MinCp
    {
        get; set;
    }

    [Range(0, 10000)]
    public int? MaxCp
    {
        get; set;
    }

    [Range(0, 55)]
    public int? MinLevel
    {
        get; set;
    }

    [Range(0, 55)]
    public int? MaxLevel
    {
        get; set;
    }

    [Range(0, int.MaxValue)]
    public int? MinWeight
    {
        get; set;
    }

    [Range(0, int.MaxValue)]
    public int? MaxWeight
    {
        get; set;
    }

    [Range(0, 15)]
    public int? Atk
    {
        get; set;
    }

    [Range(0, 15)]
    public int? Def
    {
        get; set;
    }

    [Range(0, 15)]
    public int? Sta
    {
        get; set;
    }

    [Range(0, 15)]
    public int? MaxAtk
    {
        get; set;
    }

    [Range(0, 15)]
    public int? MaxDef
    {
        get; set;
    }

    [Range(0, 15)]
    public int? MaxSta
    {
        get; set;
    }

    [Range(0, 4096)]
    public int? PvpRankingWorst
    {
        get; set;
    }

    [Range(0, 4096)]
    public int? PvpRankingBest
    {
        get; set;
    }

    [Range(0, 10000)]
    public int? PvpRankingMinCp
    {
        get; set;
    }

    // The same four the create DTO allows (#586). Left as an unbounded range here, an edit could store a
    // CP cap no league uses -- the exact state that fix was written to prevent, reached from the edit
    // path instead. See #594.
    [AllowedValues(null, 0, 500, 1500, 2500)]
    public int? PvpRankingLeague
    {
        get; set;
    }

    [Range(0, 55)]
    public int? PvpRankingCap
    {
        get; set;
    }

    [Range(0, int.MaxValue)]
    public int? Form
    {
        get; set;
    }

    [Range(-1, 5)]
    public int? Size
    {
        get; set;
    }

    [Range(0, 5)]
    public int? MaxSize
    {
        get; set;
    }

    [Range(0, 3)]
    public int? Gender
    {
        get; set;
    }

    // clean is a PoracleNG bitmask: bit 1 = auto-delete, bit 2 = edit-in-place, bit 4 = summary.
    [Range(0, 7)]
    public int? Clean
    {
        get; set;
    }

    [StringLength(256)]
    public string? Template
    {
        get; set;
    }

    /// <summary>Saved-place label this alarm measures its radius from. See the domain model.</summary>
    [StringLength(64)]
    public string? OverrideLocationLabel
    {
        get; set;
    }

    /// <summary>Areas this alarm is confined to. See the domain model.</summary>
    [MaxLength(32)]
    public List<string>? OverrideAreas
    {
        get; set;
    }
}
