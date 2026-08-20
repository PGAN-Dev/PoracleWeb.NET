using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations;

namespace Pgan.PoracleWebNet.Core.Models;

public class FortChangeCreate
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

    [StringLength(256)]
    [AllowedStringValues(FortChangeOptions.FortTypePokestop, FortChangeOptions.FortTypeGym, FortChangeOptions.FortTypeEverything)]
    public string? FortType
    {
        get; set;
    }

    [Range(0, 1)]
    public int IncludeEmpty
    {
        get; set;
    }

    [AllowedStringValues(
        FortChangeOptions.ChangeTypeName,
        FortChangeOptions.ChangeTypeLocation,
        FortChangeOptions.ChangeTypeImageUrl,
        FortChangeOptions.ChangeTypeRemoval,
        FortChangeOptions.ChangeTypeNew)]
    // PoracleNG stores this as its JSON text, so an export carries change_types as the STRING
    // ["name"] rather than an array -- and a genuine, unmodified backup then failed to re-import once
    // #548 started binding this DTO. The domain model has carried the converter for exactly this
    // reason; the Create DTO needs it too. See #556.
    // There are exactly five legal change types, so anything longer is impossible input and was reaching
    // the database as an over-long JSON string -- a 500 for a request the API can describe. Duplicates
    // are refused for the same reason: they cannot mean anything. See #612.
    [MaxLength(5, ErrorMessage = "changeTypes may contain at most 5 entries.")]
    [DistinctValues(ErrorMessage = "changeTypes must not repeat a value.")]
    [JsonConverter(typeof(StringOrArrayConverter))]
    public List<string> ChangeTypes { get; set; } = [];

    // clean is a PoracleNG bitmask: bit 1 = auto-delete, bit 2 = edit-in-place, bit 4 = summary.

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
