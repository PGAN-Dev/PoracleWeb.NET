using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Core.Abstractions.Repositories;

public interface IUserGeofenceRepository
{
    Task<List<UserGeofence>> GetByHumanIdAsync(string humanId);
    Task<UserGeofence?> GetByIdAsync(int id);
    Task<UserGeofence?> GetByKojiNameAsync(string kojiName);
    Task<int> GetCountByHumanIdAsync(string humanId);
    Task<List<UserGeofence>> GetByStatusAsync(string status);
    Task<UserGeofence> CreateAsync(UserGeofence geofence);
    Task<UserGeofence> UpdateAsync(UserGeofence geofence);
    Task DeleteAsync(int id);
}
