using System.ComponentModel.DataAnnotations;

namespace Pgan.PoracleWebNet.Core.Models;

public class EggCreate
{
    [StringLength(256)]
    public string? Ping
    {
        get; set;
    }

    [Range(0, int.MaxValue)]
    public int Distance
    {
        get; set;
    }

    [Range(0, 4)]
    public int Team { get; set; } = 4;

    [Range(0, 10)]
    public int Level
    {
        get; set;
    }

    [Range(0, 1)]
    public int Clean
    {
        get; set;
    }

    [StringLength(256)]
    public string? Template
    {
        get; set;
    }
}
