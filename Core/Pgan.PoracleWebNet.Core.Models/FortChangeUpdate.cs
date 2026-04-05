using System.ComponentModel.DataAnnotations;

namespace Pgan.PoracleWebNet.Core.Models;

public class FortChangeUpdate
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

    [StringLength(256)]
    public string? FortType
    {
        get; set;
    }

    [Range(0, 1)]
    public int? IncludeEmpty
    {
        get; set;
    }

    public List<string>? ChangeTypes
    {
        get; set;
    }

    [Range(0, 1)]
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
