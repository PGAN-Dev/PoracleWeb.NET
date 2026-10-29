using System.Text.Json.Serialization;

namespace Pgan.PoracleWebNet.Core.Models;

public class AdminGeofence
{
    public int Id
    {
        get; set;
    }
    public string Name { get; set; } = string.Empty;
    public string Group { get; set; } = string.Empty;
    public double[][] Path { get; set; } = [];

    /// <summary>
    /// The parts of a multi-part area, one [lat,lon] ring each, in PoracleNG's <c>multipath</c> shape. A fence
    /// carries either this or <see cref="Path"/>, never both. See #1117.
    /// </summary>
    public double[][][]? Multipath
    {
        get; set;
    }

    /// <summary>Every ring a point can fall inside: the parts of a multi-part area, or the one path.</summary>
    [JsonIgnore]
    public double[][][] Rings => this.Multipath is { Length: > 0 } ? this.Multipath : [this.Path];

    public bool UserSelectable { get; set; } = true;
    public bool DisplayInMatches { get; set; } = true;
    public string Description { get; set; } = string.Empty;
    public string Color { get; set; } = "#3399ff";

    /// <summary>Polygon bounding box computed at fetch time; used as a cheap pre-filter before point-in-polygon ray-casting.</summary>
    [JsonIgnore]
    public double MinLat
    {
        get; set;
    }
    [JsonIgnore]
    public double MaxLat
    {
        get; set;
    }
    [JsonIgnore]
    public double MinLon
    {
        get; set;
    }
    [JsonIgnore]
    public double MaxLon
    {
        get; set;
    }
}
