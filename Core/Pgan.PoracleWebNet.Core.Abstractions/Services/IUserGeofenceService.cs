using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

public interface IUserGeofenceService
{
    public Task<List<UserGeofence>> GetByUserAsync(string humanId);
    public Task<UserGeofence> CreateAsync(string humanId, int profileNo, UserGeofenceCreate model);
    /// <summary>
    /// Renames a geofence and moves its area subscriptions with it.
    /// </summary>
    /// <remarks>
    /// The Geofences page used to implement editing as delete-then-recreate, and the recreate
    /// re-subscribed only the ACTIVE profile — so renaming from profile 0 quietly switched the geofence
    /// off everywhere else, while the page still showed it on. See #543.
    /// </remarks>
    public Task<UserGeofence> RenameAsync(string humanId, int id, string displayName, string? groupName, int? parentId);

    /// <summary>
    /// What deleting the caller's own fence would do to their alarms. Same ownership rules as
    /// <see cref="DeleteAsync"/>.
    /// </summary>
    public Task<AreaDeleteImpact> GetDeleteImpactAsync(string humanId, int id);

    /// <summary>
    /// Deletes the caller's own fence and removes its name from every profile and every alarm's override
    /// areas. With <paramref name="deleteWidenedAlarms"/>, alarms limited to this fence alone are deleted
    /// rather than left matching across the profile's areas. If PoracleNG cannot remove the name, the fence
    /// is not deleted.
    /// </summary>
    public Task<PairWarning?> DeleteAsync(string humanId, int profileNo, int id, bool deleteWidenedAlarms);
    public Task<UserGeofence> SubmitForReviewAsync(string humanId, string kojiName);
    public Task<List<UserGeofence>> GetAllAsync();
    public Task<List<UserGeofence>> GetAllWithDetailsAsync();
    public Task<List<UserGeofence>> GetPendingSubmissionsAsync();
    /// <summary>What deleting this fence would do to its owner's alarms.</summary>
    public Task<AreaDeleteImpact> AdminGetDeleteImpactAsync(int id);

    /// <summary>
    /// <see cref="DeleteAsync"/> for any owner. An owner PoracleNG no longer knows has no areas to clean,
    /// so the fence is deleted anyway.
    /// </summary>
    public Task<PairWarning?> AdminDeleteAsync(string adminId, int id, bool deleteWidenedAlarms);

    /// <summary>
    /// For an account that has just been deleted: removes every fence it still owns from Koji (if promoted)
    /// and from the table, then reloads PoracleNG's geofences once. Touches no area lists, because the
    /// account's went with it. Used by the user purge for fences whose normal delete failed.
    /// </summary>
    public Task RemoveFencesOfDeletedAccountAsync(string humanId);
    public Task<UserGeofence> ApproveSubmissionAsync(string adminId, int id, string? promotedName, int? parentId = null, string? groupName = null);
    public Task<UserGeofence> RejectSubmissionAsync(string adminId, int id, string reviewNotes);
    public Task<PairWarning?> AddToProfileAsync(string humanId, int profileNo, int geofenceId);
    public Task<PairWarning?> RemoveFromProfileAsync(string humanId, int profileNo, int geofenceId);
    public Task<List<GeofenceRegion>> GetRegionsAsync();
}
