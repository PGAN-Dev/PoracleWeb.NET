using System.ComponentModel.DataAnnotations;

namespace PGAN.Poracle.Web.Core.Models;

public class QuestUpdate
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

    [Range(0, int.MaxValue)]
    public int Form
    {
        get; set;
    }
}
