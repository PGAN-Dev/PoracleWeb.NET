using System.ComponentModel.DataAnnotations;

namespace PGAN.Poracle.Web.Core.Models;

public class RaidCreate
{
    [Range(0, int.MaxValue)]
    public int PokemonId
    {
        get; set;
    }

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

    [Range(0, 10)]
    public int Level
    {
        get; set;
    }

    [Range(0, int.MaxValue)]
    public int Form
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
