using System.Text.Json;

namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

/// <summary>
/// Proxies human and profile management operations to the PoracleNG REST API.
/// Replaces direct writes to the humans and profiles tables.
/// </summary>
public interface IPoracleHumanProxy
{
    /// <summary>
    /// Fetches a single human record.
    /// Maps to GET /api/humans/one/{userId}
    /// </summary>
    public Task<JsonElement?> GetHumanAsync(string userId);

    /// <summary>
    /// Creates a new human record.
    /// Maps to POST /api/humans
    /// </summary>
    public Task CreateHumanAsync(JsonElement body);

    /// <summary>
    /// Enables alerts for a user.
    /// Maps to POST /api/humans/{userId}/start
    /// </summary>
    public Task StartAsync(string userId);

    /// <summary>
    /// Disables alerts for a user.
    /// Maps to POST /api/humans/{userId}/stop
    /// </summary>
    public Task StopAsync(string userId);

    /// <summary>
    /// Admin-disables or re-enables a user.
    /// Maps to POST /api/humans/{userId}/adminDisabled
    /// </summary>
    public Task AdminDisabledAsync(string userId, bool disabled);

    /// <summary>
    /// Sets user location.
    /// Maps to POST /api/humans/{userId}/setLocation/{lat}/{lon}
    /// </summary>
    public Task SetLocationAsync(string userId, double lat, double lon);

    /// <summary>
    /// Sets user area subscriptions. PoracleNG handles the dual-write to
    /// humans.area + profiles.area atomically.
    /// Maps to POST /api/humans/{userId}/setAreas
    /// </summary>
    public Task SetAreasAsync(string userId, string[] areas);

    /// <summary>
    /// Fetches the user's current area subscriptions.
    /// Maps to GET /api/humans/{userId}
    /// </summary>
    public Task<JsonElement?> GetAreasAsync(string userId);

    /// <summary>
    /// Switches the user's active profile. PoracleNG handles the area save/load
    /// dual-write atomically.
    /// Maps to POST /api/humans/{userId}/switchProfile/{profileNo}
    /// </summary>
    public Task SwitchProfileAsync(string userId, int profileNo);

    /// <summary>
    /// Fetches all profiles for a user.
    /// Maps to GET /api/profiles/{userId}
    /// </summary>
    public Task<JsonElement> GetProfilesAsync(string userId);

    /// <summary>
    /// Creates a new profile.
    /// Maps to POST /api/profiles/{userId}/add
    /// </summary>
    public Task AddProfileAsync(string userId, JsonElement body);

    /// <summary>
    /// Updates a profile (name, etc.).
    /// Maps to POST /api/profiles/{userId}/update
    /// </summary>
    public Task UpdateProfileAsync(string userId, JsonElement body);

    /// <summary>
    /// Deletes a profile. PoracleNG may cascade-delete alarms.
    /// Maps to DELETE /api/profiles/{userId}/byProfileNo/{profileNo}
    /// </summary>
    public Task DeleteProfileAsync(string userId, int profileNo);

    /// <summary>
    /// Checks if a location is inside any geofence.
    /// Maps to GET /api/humans/{userId}/checkLocation/{lat}/{lon}
    /// </summary>
    public Task<JsonElement?> CheckLocationAsync(string userId, double lat, double lon);

    /// <summary>
    /// Copies all tracking rules from one profile to another.
    /// Maps to POST /api/profiles/{userId}/copy/{fromProfileNo}/{toProfileNo}
    /// </summary>
    public Task CopyProfileAsync(string userId, int fromProfileNo, int toProfileNo);
}
