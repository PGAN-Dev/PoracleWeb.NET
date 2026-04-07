using System.Text.Json;

namespace Pgan.PoracleWebNet.Core.Models;

public class GeoJsonFeatureCollection
{
    public string Type { get; set; } = "FeatureCollection";
    public List<GeoJsonFeature> Features { get; set; } = [];
}

public class GeoJsonFeature
{
    public string Type { get; set; } = "Feature";
    public GeoJsonGeometry Geometry { get; set; } = new();
    public Dictionary<string, JsonElement>? Properties
    {
        get; set;
    }
}

public class GeoJsonGeometry
{
    public string Type { get; set; } = string.Empty;
    public JsonElement Coordinates
    {
        get; set;
    }
}
