using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Abstractions.Repositories;

public interface IUserGeofenceRepository
{
    public Task<List<UserGeofence>> GetByHumanIdAsync(string humanId);
    public Task<UserGeofence?> GetByIdAsync(int id);
    public Task<UserGeofence?> GetByKojiNameAsync(string kojiName);
    public Task<int> GetCountByHumanIdAsync(string humanId);
    public Task<List<UserGeofence>> GetByStatusAsync(string status);
    public Task<List<UserGeofence>> GetAllActiveAsync();
    public Task<List<UserGeofence>> GetAllAsync();
    public Task<UserGeofence> CreateAsync(UserGeofence geofence);
    public Task<UserGeofence> UpdateAsync(UserGeofence geofence);
    public Task DeleteAsync(int id);
}
