using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Core.Abstractions.Services;

public interface IKojiService
{
    public Task SaveGeofenceAsync(string geofenceName, string displayName, string group, int parentId, double[][] polygon);
    public Task RemoveGeofenceFromProjectAsync(string geofenceName);
    public Task<List<GeofenceRegion>> GetRegionsAsync();
    public Task<List<UserGeofence>> GetUserGeofencesAsync(string humanId);

    /// <summary>
    /// Promotes a user geofence to be publicly visible by setting userSelectable and displayInMatches to true.
    /// Optionally renames it (drops the pweb_ prefix).
    /// </summary>
    Task PromoteGeofenceAsync(string currentName, string? newName, string displayName, string group, int parentId);

    /// <summary>
    /// Gets the polygon coordinates for a specific geofence from Koji.
    /// Returns [lat,lon] pairs or null if not found.
    /// </summary>
    Task<double[][]?> GetGeofencePolygonAsync(string geofenceName);
}
