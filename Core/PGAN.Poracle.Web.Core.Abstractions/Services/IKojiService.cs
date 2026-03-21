using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Core.Abstractions.Services;

public interface IKojiService
{
    public Task SaveGeofenceAsync(string geofenceName, string displayName, string group, int parentId, double[][] polygon);
    public Task RemoveGeofenceFromProjectAsync(string geofenceName, double[][] polygon);
    public Task<List<GeofenceRegion>> GetRegionsAsync();
}
