using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Core.Abstractions.Services;

public interface IKojiService
{
    public Task SaveGeofenceAsync(string geofenceName, string displayName, string group, int parentId, double[][] polygon, bool isPublic = false);
    public Task RemoveGeofenceFromProjectAsync(string geofenceName);
    public Task<List<GeofenceRegion>> GetRegionsAsync();

    /// <summary>
    /// Gets the polygon coordinates for a specific geofence from Koji.
    /// Returns [lat,lon] pairs or null if not found.
    /// </summary>
    public Task<double[][]?> GetGeofencePolygonAsync(string geofenceName);
}
