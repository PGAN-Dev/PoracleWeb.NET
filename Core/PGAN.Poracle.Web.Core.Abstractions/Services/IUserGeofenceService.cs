using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Core.Abstractions.Services;

public interface IUserGeofenceService
{
    Task<List<UserGeofence>> GetByUserAsync(string humanId);
    Task<UserGeofence> CreateAsync(string humanId, int profileNo, UserGeofenceCreate model);
    Task DeleteAsync(string humanId, int profileNo, int id);
    Task<UserGeofence> SubmitForReviewAsync(string humanId, string kojiName);
    Task<List<UserGeofence>> GetPendingSubmissionsAsync();
    Task<UserGeofence> ApproveSubmissionAsync(string adminId, int id, string? promotedName);
    Task<UserGeofence> RejectSubmissionAsync(string adminId, int id, string reviewNotes);
    Task<List<GeofenceRegion>> GetRegionsAsync();
}
