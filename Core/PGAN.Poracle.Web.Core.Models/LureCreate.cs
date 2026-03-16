using System.ComponentModel.DataAnnotations;

namespace PGAN.Poracle.Web.Core.Models;

public class LureCreate
{
    [StringLength(256)]
    public string? Ping { get; set; }

    [Range(0, int.MaxValue)]
    public int Distance { get; set; }

    [Range(0, int.MaxValue)]
    public int LureId { get; set; }

    [Range(0, 1)]
    public int Clean { get; set; }

    [StringLength(256)]
    public string? Template { get; set; }

    [Range(1, int.MaxValue)]
    public int ProfileNo { get; set; }
}
