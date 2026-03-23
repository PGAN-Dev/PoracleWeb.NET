using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

public interface IUserGeofenceService
{
    public Task<List<UserGeofence>> GetByUserAsync(string humanId);
    public Task<UserGeofence> CreateAsync(string humanId, int profileNo, UserGeofenceCreate model);
    public Task DeleteAsync(string humanId, int profileNo, int id);
    public Task<UserGeofence> SubmitForReviewAsync(string humanId, string kojiName);
    public Task<List<UserGeofence>> GetAllAsync();
    public Task<List<UserGeofence>> GetPendingSubmissionsAsync();
    public Task AdminDeleteAsync(string adminId, int id);
    public Task<UserGeofence> ApproveSubmissionAsync(string adminId, int id, string? promotedName);
    public Task<UserGeofence> RejectSubmissionAsync(string adminId, int id, string reviewNotes);
    public Task<List<GeofenceRegion>> GetRegionsAsync();
}
