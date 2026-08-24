using Pgan.PoracleWebNet.Core.Models;
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
    /// GET /api/v2/humans/{userId}, or GET /api/humans/one/{userId} on a PoracleNG without v2.
    /// </summary>
    public Task<JsonElement?> GetHumanAsync(string userId);

    /// <summary>
    /// Creates a new human record.
    /// Maps to POST /api/humans
    /// </summary>
    public Task CreateHumanAsync(JsonElement body);

    /// <summary>
    /// Enables alerts for a user.
    /// POST /api/v2/humans/{userId}/enable, or POST /api/humans/{userId}/start without v2.
    /// </summary>
    public Task StartAsync(string userId);

    /// <summary>
    /// Disables alerts for a user.
    /// POST /api/v2/humans/{userId}/disable, or POST /api/humans/{userId}/stop without v2.
    /// </summary>
    public Task StopAsync(string userId);

    /// <summary>
    /// Sets the language PoracleNG writes this user's alerts in.
    /// </summary>
    /// <remarks>
    /// POST /api/v2/humans/{userId}/language, falling back to POST /api/humans/{userId}/language, which
    /// exists on both supported releases. Replaces a direct write to <c>humans.language</c>: the handler
    /// also reloads PoracleNG's in-memory state, which the direct write never did, so a language change
    /// now takes effect on the next alert instead of at the next restart.
    /// <para>
    /// Both handlers lowercase and trim what they store, so <c>pt-BR</c> comes back as <c>pt-br</c>.
    /// Verified on 5.2.1 against v1 and v2 alike.
    /// </para>
    /// </remarks>
    public Task SetLanguageAsync(string userId, string language);

    /// <summary>
    /// Admin-disables or re-enables a user.
    /// Maps to POST /api/humans/{userId}/adminDisabled
    /// </summary>
    public Task AdminDisabledAsync(string userId, bool disabled);

    /// <summary>
    /// Sets user location.
    /// POST /api/v2/humans/{userId}/location with a {lat,lon} body, or v1's coordinates-in-the-path form.
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
    /// Copies all tracking rules from one profile to another.
    /// Maps to POST /api/profiles/{userId}/copy/{fromProfileNo}/{toProfileNo}
    /// </summary>
    public Task CopyProfileAsync(string userId, int fromProfileNo, int toProfileNo);

    /// <summary>
    /// The user's saved places, plus the profile pin every alarm falls back to.
    /// Maps to GET /api/humans/{id}/locations
    /// </summary>
    public Task<SavedPlaces> GetPlacesAsync(string userId);

    /// <summary>
    /// Saves a place. PoracleNG reports per-row outcomes rather than failing the request, so a
    /// duplicate label comes back as a message here rather than an exception.
    /// Maps to POST /api/humans/{id}/locations/add
    /// </summary>
    /// <returns>Null on success, or PoracleNG's reason for refusing this label.</returns>
    public Task<string?> AddPlaceAsync(string userId, SavedPlace place);

    /// <summary>
    /// Moves a saved place, keeping its label so every alarm pointing at it follows.
    /// </summary>
    /// <remarks>
    /// PUT /api/v2/humans/{id}/locations/{label}. There is no v1 equivalent, which is why moving a place
    /// an alarm referenced was impossible before: the delete answers 409 while anything still points at
    /// it, so the only route was to repoint every alarm, delete, re-add and repoint back.
    /// </remarks>
    /// <returns>False when this PoracleNG has no such route; the caller should say so rather than retry.</returns>
    public Task<bool> UpdatePlaceAsync(string userId, string label, double latitude, double longitude);

    /// <summary>
    /// Deletes a saved place.
    /// Maps to POST /api/humans/{id}/locations/{label}/delete
    /// </summary>
    /// <exception cref="PlaceInUseException">Alarms still point at this place.</exception>
    public Task DeletePlaceAsync(string userId, string label);
}
