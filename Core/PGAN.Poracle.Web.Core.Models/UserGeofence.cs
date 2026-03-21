namespace PGAN.Poracle.Web.Core.Models;

public class UserGeofence
{
    public int Id { get; set; }
    public string HumanId { get; set; } = string.Empty;
    public int ProfileNo { get; set; }
    public string GeofenceName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string GroupName { get; set; } = string.Empty;
    public int ParentId { get; set; }
    public string PolygonJson { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
