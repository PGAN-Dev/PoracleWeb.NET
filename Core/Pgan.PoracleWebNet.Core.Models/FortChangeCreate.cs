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
    public List<string> ChangeTypes { get; set; } = [];

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
