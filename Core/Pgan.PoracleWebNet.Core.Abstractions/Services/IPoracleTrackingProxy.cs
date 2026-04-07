using System.Text.Json;

namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

/// <summary>
/// Proxies alarm tracking CRUD operations to the PoracleNG REST API instead of writing
/// directly to the Poracle database. PoracleNG handles field defaults, dedup, and
/// immediate state reload on every mutation.
/// </summary>
public interface IPoracleTrackingProxy
{
    /// <summary>
    /// Fetches all tracking alarms of a type for a user on their active profile.
    /// Maps to GET /api/tracking/{type}/{userId}
    /// </summary>
    public Task<JsonElement> GetByUserAsync(string type, string userId);

    /// <summary>
    /// Creates one or more tracking alarms. PoracleNG applies cleanRow defaults,
    /// detects duplicates, and triggers state reload.
    /// Maps to POST /api/tracking/{type}/{userId}?silent=true
    /// Returns the list of created/updated UIDs.
    /// </summary>
    public Task<TrackingCreateResult> CreateAsync(string type, string userId, JsonElement body);

    /// <summary>
    /// Deletes a single tracking alarm by UID.
    /// Maps to DELETE /api/tracking/{type}/{userId}/byUid/{uid}
    /// </summary>
    public Task DeleteByUidAsync(string type, string userId, int uid);

    /// <summary>
    /// Deletes multiple tracking alarms by UID list.
    /// Maps to POST /api/tracking/{type}/{userId}/delete
    /// </summary>
    public Task BulkDeleteByUidsAsync(string type, string userId, IEnumerable<int> uids);

    /// <summary>
    /// Fetches all tracking alarms across all types for a user on their active profile.
    /// Maps to GET /api/tracking/all/{userId}
    /// </summary>
    public Task<JsonElement> GetAllTrackingAsync(string userId);

    /// <summary>
    /// Fetches all tracking rules across all profiles for a user.
    /// Maps to GET /api/tracking/allProfiles/{userId}?includeDescriptions=true
    /// </summary>
    public Task<JsonElement> GetAllTrackingAllProfilesAsync(string userId);

    /// <summary>
    /// Triggers a state reload in PoracleNG.
    /// Maps to GET /api/reload
    /// </summary>
    public Task ReloadStateAsync();
}

/// <summary>
/// Result from PoracleNG's tracking create endpoint.
/// </summary>
public record TrackingCreateResult(
    List<long> NewUids,
    int AlreadyPresent,
    int Updates,
    int Inserts);
