using System.ComponentModel.DataAnnotations;

namespace Pgan.PoracleWebNet.Core.Models;

public class LureUpdate
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

    [Range(0, int.MaxValue)]
    public int? LureId
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
}
