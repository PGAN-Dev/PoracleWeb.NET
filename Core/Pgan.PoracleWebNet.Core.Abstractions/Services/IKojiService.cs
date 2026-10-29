using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

public interface IKojiService
{
    public Task SaveGeofenceAsync(string geofenceName, string displayName, string group, int parentId, double[][] polygon, bool isPublic = false);
    public Task RemoveGeofenceFromProjectAsync(string geofenceName);
    public Task<List<GeofenceRegion>> GetRegionsAsync();

    /// <summary>
    /// Gets admin geofences from Koji in Poracle format with groups resolved from parent chain.
    /// Results are cached in memory with a 5-minute TTL.
    /// </summary>
    public Task<List<AdminGeofence>> GetAdminGeofencesAsync();

    /// <summary>
    /// Invalidates the cached admin geofences so the next call to GetAdminGeofencesAsync fetches fresh data.
    /// </summary>
    public void InvalidateAdminGeofenceCache();
}
