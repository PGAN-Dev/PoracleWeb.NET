using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Core.Abstractions.Services;

public interface IUserGeofenceService
{
    Task<List<UserGeofence>> GetByUserAsync(string humanId, int profileNo);
    Task<UserGeofence> CreateAsync(string humanId, int profileNo, UserGeofenceCreate model);
    Task<UserGeofence> UpdateAsync(string humanId, int id, UserGeofenceCreate model);
    Task DeleteAsync(string humanId, int profileNo, int id);
    Task<List<GeofenceRegion>> GetRegionsAsync();
}
