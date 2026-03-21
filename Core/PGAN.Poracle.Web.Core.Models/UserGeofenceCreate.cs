namespace PGAN.Poracle.Web.Core.Models;

public class UserGeofenceCreate
{
    public string DisplayName { get; set; } = string.Empty;
    public string GroupName { get; set; } = string.Empty;
    public int ParentId { get; set; }
    public double[][] Polygon { get; set; } = [];
}
