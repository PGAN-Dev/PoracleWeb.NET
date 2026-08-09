namespace Pgan.PoracleWebNet.Core.Models;

public class GeoJsonImportResult
{
    public List<UserGeofence> Created { get; set; } = [];
    public List<GeoJsonImportError> Errors { get; set; } = [];

    /// <summary>
    /// Features that were imported, but not exactly as the file described them. Separate from
    /// <see cref="Errors"/> because these did produce a geofence - the user still needs telling,
    /// since the stored shape is what PoracleJS matches alerts against. See #474.
    /// </summary>
    public List<GeoJsonImportError> Warnings { get; set; } = [];
}

public class GeoJsonImportError
{
    public string FeatureName { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}
