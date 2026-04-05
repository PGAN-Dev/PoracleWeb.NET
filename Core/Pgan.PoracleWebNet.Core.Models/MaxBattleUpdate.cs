using System.ComponentModel.DataAnnotations;

namespace Pgan.PoracleWebNet.Core.Models;

public class MaxBattleUpdate
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

    [Range(0, 1)]
    public int? Gmax
    {
        get; set;
    }

    [Range(0, int.MaxValue)]
    public int? Level
    {
        get; set;
    }

    [Range(0, int.MaxValue)]
    public int? Form
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

    [Range(0, int.MaxValue)]
    public int? Move
    {
        get; set;
    }

    [Range(0, int.MaxValue)]
    public int? Evolution
    {
        get; set;
    }

    [StringLength(255)]
    public string? StationId
    {
        get; set;
    }
}
