using System.ComponentModel.DataAnnotations;

namespace Pgan.PoracleWebNet.Core.Models;

public class QuestCreate
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

    [Range(0, int.MaxValue)]
    public int Reward
    {
        get; set;
    }

    [Range(0, int.MaxValue)]
    public int RewardType
    {
        get; set;
    }

    [Range(0, 1)]
    public int Shiny
    {
        get; set;
    }

    // clean is a PoracleNG bitmask: bit 1 = auto-delete, bit 2 = edit-in-place, bit 4 = summary.
    [Range(0, 7)]
    public int Clean
    {
        get; set;
    }

    [StringLength(256)]
    public string? Template
    {
        get; set;
    }

    [Range(0, int.MaxValue)]
    public int Form
    {
        get; set;
    }
}
