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

    [Range(0, int.MaxValue)]
    public int? PvpRankingLeague
    {
        get; set;
    }

    [Range(0, int.MaxValue)]
    public int? Form
    {
        get; set;
    }

    [Range(0, 5)]
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

    [Range(0, 1)]
    public int? Clean
    {
        get; set;
    }

    [StringLength(256)]
    public string? Template
    {
        get; set;
    }
}
