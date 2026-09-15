using System.ComponentModel.DataAnnotations;

namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>Null-skip update body for a pokestop-event alarm. See <see cref="PokestopEventCreate"/>.</summary>
public class PokestopEventUpdate
{
    [Range(1, int.MaxValue)]
    public int? DisplayType
    {
        get; set;
    }

    [Range(0, int.MaxValue)]
    public int? Distance
    {
        get; set;
    }

    [StringLength(256)]
    public string? Template
    {
        get; set;
    }

    [Range(0, 7)]
    public int? Clean
    {
        get; set;
    }

    [StringLength(64)]
    public string? OverrideLocationLabel
    {
        get; set;
    }

    [MaxLength(32)]
    public List<string>? OverrideAreas
    {
        get; set;
    }
}
