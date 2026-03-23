using System.ComponentModel.DataAnnotations;

namespace Pgan.PoracleWebNet.Core.Models;

public class UserGeofenceCreate
{
    [Required]
    [MaxLength(50)]
    public string DisplayName { get; set; } = string.Empty;

    public string GroupName { get; set; } = string.Empty;
    public int ParentId
    {
        get; set;
    }
    public double[][] Polygon { get; set; } = [];
}
