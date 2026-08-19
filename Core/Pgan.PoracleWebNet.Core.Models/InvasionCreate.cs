using System.ComponentModel.DataAnnotations;

namespace Pgan.PoracleWebNet.Core.Models;

public class InvasionCreate
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

    [Range(0, 3)]
    public int Gender
    {
        get; set;
    }

    /// <summary>
    /// Required. PoracleNG has no catch-all — an empty value is rejected upstream, so accepting one
    /// here only converted a clear 400 into an opaque 500. Track everything by posting one alarm per
    /// grunt type. See #416.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    [StringLength(256)]
    public string? GruntType
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

    /// <summary>Saved-place label this alarm measures its radius from. See the domain model.</summary>
    [StringLength(64)]
    public string? OverrideLocationLabel
    {
        get; set;
    }

    /// <summary>Areas this alarm is confined to. See the domain model.</summary>
    [MaxLength(32)]
    public List<string>? OverrideAreas
    {
        get; set;
    }
}
