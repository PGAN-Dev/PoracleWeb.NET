using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

public interface IUserGeofenceService
{
    public Task<List<UserGeofence>> GetByUserAsync(string humanId);
    public Task<UserGeofence> CreateAsync(string humanId, int profileNo, UserGeofenceCreate model);
    public Task DeleteAsync(string humanId, int profileNo, int id);
    public Task<UserGeofence> SubmitForReviewAsync(string humanId, string kojiName);
    public Task<List<UserGeofence>> GetAllAsync();
    public Task<List<UserGeofence>> GetAllWithDetailsAsync();
    public Task<List<UserGeofence>> GetPendingSubmissionsAsync();
    public Task AdminDeleteAsync(string adminId, int id);
    public Task<UserGeofence> ApproveSubmissionAsync(string adminId, int id, string? promotedName);
    public Task<UserGeofence> RejectSubmissionAsync(string adminId, int id, string reviewNotes);
    public Task AddToProfileAsync(string humanId, int profileNo, int geofenceId);
    public Task RemoveFromProfileAsync(string humanId, int profileNo, int geofenceId);
    public Task<List<GeofenceRegion>> GetRegionsAsync();

    /// <summary>
    /// Given a list of requested area names, re-adds any that correspond to user-owned custom
    /// geofences back into <c>humans.area</c> and the current <c>profiles.area</c> via direct DB.
    /// Used to undo PoracleNG's setAreas filter which silently drops userSelectable=false fences.
    /// Returns the subset of names that were preserved.
    /// </summary>
    public Task<IReadOnlyList<string>> PreserveOwnedAreasInHumanAsync(string humanId, IReadOnlyCollection<string> candidateAreaNames);
}
