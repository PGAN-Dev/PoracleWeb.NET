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
