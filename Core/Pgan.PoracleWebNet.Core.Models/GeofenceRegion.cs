namespace Pgan.PoracleWebNet.Core.Models;

public class GeofenceRegion
{
    public int Id
    {
        get; set;
    }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public double[][]? Polygon
    {
        get; set;
    }

    /// <summary>The parts of a multi-part region, one [lat,lon] outer ring each. Set instead of <see cref="Polygon"/>.</summary>
    public double[][][]? Multipath
    {
        get; set;
    }
}
