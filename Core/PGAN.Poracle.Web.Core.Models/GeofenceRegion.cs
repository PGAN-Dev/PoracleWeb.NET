namespace PGAN.Poracle.Web.Core.Models;

public class GeofenceRegion
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public double[][]? Polygon { get; set; }
}
