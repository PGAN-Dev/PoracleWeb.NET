namespace PGAN.Poracle.Web.Core.Models;

public class UserGeofence
{
    public int Id { get; set; }
    public string HumanId { get; set; } = string.Empty;
    public string KojiName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string GroupName { get; set; } = string.Empty;
    public int ParentId { get; set; }
    public string Status { get; set; } = "active";
    public DateTime? SubmittedAt { get; set; }
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewNotes { get; set; }
    public string? PromotedName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Polygon data from Koji (not stored in DB)
    public double[][]? Polygon { get; set; }
}
