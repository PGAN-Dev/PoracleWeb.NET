using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Core.Abstractions.Repositories;

public interface IUserGeofenceRepository
{
    Task<List<UserGeofence>> GetByHumanIdAsync(string humanId, int profileNo);
    Task<UserGeofence?> GetByIdAsync(int id);
    Task<int> GetCountByHumanIdAsync(string humanId);
    Task<UserGeofence> CreateAsync(UserGeofence geofence);
    Task<UserGeofence> UpdateAsync(UserGeofence geofence);
    Task DeleteAsync(int id);
}
