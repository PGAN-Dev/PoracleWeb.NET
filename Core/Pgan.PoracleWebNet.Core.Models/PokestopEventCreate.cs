using System.ComponentModel.DataAnnotations;

namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>
/// The body <c>PokestopEventController.Create</c> binds. Validation attributes live here, not on
/// <see cref="PokestopEvent"/> — anything validating an alarm outside model binding must bind this
/// DTO or it validates nothing at all.
/// </summary>
public class PokestopEventCreate
{
    /// <summary>
    /// The pokestop-event id. Required, with no wildcard: upstream has no "any event" rule.
    /// </summary>
    /// <remarks>
    /// Deliberately bounded rather than enumerated. An id upstream knows and this build does not is
    /// refused by PoracleNG's own 422 naming the unknown display_type; an allowlist built from
    /// <see cref="PokestopEventTypes"/> would refuse it here instead, making a new event un-creatable
    /// the moment upstream ships it.
    /// </remarks>
    [Range(1, int.MaxValue)]
    public int DisplayType
    {
        get; set;
    }

    [Range(0, int.MaxValue)]
    public int Distance
    {
        get; set;
    }

    [StringLength(256)]
    public string? Template
    {
        get; set;
    }

    /// <summary>The PoracleNG clean bitmask: bit 1 auto-delete, bit 2 edit-in-place, bit 4 summary.</summary>
    [Range(0, 7)]
    public int Clean
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
