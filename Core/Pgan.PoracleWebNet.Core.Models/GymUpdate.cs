using System.ComponentModel.DataAnnotations;

namespace Pgan.PoracleWebNet.Core.Models;

public class GymUpdate
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

    [Range(0, 4)]
    public int? Team
    {
        get; set;
    }

    [Range(0, 1)]
    public int? SlotChanges
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

    [Range(0, 1)]
    public int? BattleChanges
    {
        get; set;
    }

    [StringLength(255)]
    public string? GymId
    {
        get; set;
    }
}
