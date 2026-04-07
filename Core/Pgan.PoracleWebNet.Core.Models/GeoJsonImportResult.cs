namespace Pgan.PoracleWebNet.Core.Models;

public class GeoJsonImportResult
{
    public List<UserGeofence> Created { get; set; } = [];
    public List<GeoJsonImportError> Errors { get; set; } = [];
}

public class GeoJsonImportError
{
    public string FeatureName { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}
