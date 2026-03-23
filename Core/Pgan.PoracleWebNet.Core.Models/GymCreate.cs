using System.ComponentModel.DataAnnotations;

namespace Pgan.PoracleWebNet.Core.Models;

public class GymCreate
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
    public int Team
    {
        get; set;
    }

    [Range(0, 1)]
    public int SlotChanges
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
