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
    /// GET /api/v2/humans/{userId}.
    /// </summary>
    /// <returns>The human, or null when PoracleNG answers 404: it has no such human.</returns>
    /// <exception cref="HttpRequestException">
    /// PoracleNG answered anything else non-2xx, or could not be reached. The caller must not read that
    /// as "this account does not exist". It used to answer null here too, which is how an outage was
    /// reported as a deleted account. See #994.
    /// </exception>
    public Task<JsonElement?> GetHumanAsync(string userId);

    /// <summary>
    /// Creates a new human record.
    /// Maps to POST /api/v2/humans
    /// </summary>
    public Task CreateHumanAsync(JsonElement body);

    /// <summary>
    /// Enables alerts for a user.
    /// POST /api/v2/humans/{userId}/enable.
    /// </summary>
    public Task StartAsync(string userId);

    /// <summary>
    /// Disables alerts for a user.
    /// POST /api/v2/humans/{userId}/disable.
    /// </summary>
    public Task StopAsync(string userId);

    /// <summary>
    /// Sets the language PoracleNG writes this user's alerts in.
    /// </summary>
    /// <remarks>
    /// POST /api/v2/humans/{userId}/language. Replaces a direct write to <c>humans.language</c>: the handler
    /// also reloads PoracleNG's in-memory state, which the direct write never did, so a language change
    /// now takes effect on the next alert instead of at the next restart.
    /// <para>
    /// Both handlers lowercase and trim what they store, so <c>pt-BR</c> comes back as <c>pt-br</c>.
    /// Verified on 5.2.1 and 5.4.0.
    /// </para>
    /// </remarks>
    public Task SetLanguageAsync(string userId, string language);

    /// <summary>
    /// Admin-disables or re-enables a user.
    /// Maps to POST /api/v2/humans/{userId}/admin-disable
    /// </summary>
    public Task AdminDisabledAsync(string userId, bool disabled);

    /// <summary>
    /// Sets user location.
    /// POST /api/v2/humans/{userId}/location with a {latitude,longitude} body.
    /// </summary>
    public Task SetLocationAsync(string userId, double lat, double lon);

    /// <summary>
    /// The channels, webhooks and users this human may administer on Discord and Telegram.
    /// </summary>
    /// <remarks>
    /// GET /api/v2/humans/{userId}/admin-roles.
    /// <para>
    /// This sits on the delegated-webhook path, which has broken four separate times by one surface
    /// disagreeing with another (#564, #601, #626, #786), so the distinction below is load-bearing: an
    /// empty answer and an unknown answer must not look the same to the caller.
    /// </para>
    /// </remarks>
    /// <returns>The roles JSON, or null when PoracleNG says it has no such human.</returns>
    /// <exception cref="HttpRequestException">
    /// Upstream is degraded. The caller must not read that as "this user administers nothing" -- doing
    /// so denies a legitimate delegate for the whole cache TTL.
    /// </exception>
    public Task<string?> GetAdminRolesAsync(string userId);

    /// <summary>
    /// Sets user area subscriptions. PoracleNG handles the dual-write to
    /// humans.area + profiles.area atomically.
    /// Maps to POST /api/v2/humans/{userId}/areas (never with <c>trusted</c>).
    /// </summary>
    /// <remarks>
    /// PoracleNG leaves out names this user may not select, including every user-drawn fence.
    /// That is not an error; the caller re-adds the user's own fences afterwards.
    /// </remarks>
    public Task SetAreasAsync(string userId, string[] areas);

    /// <summary>
    /// Fetches the user's current area subscriptions: the human record, read the same way as
    /// <see cref="GetHumanAsync"/>, with the same null-on-404, throw-otherwise contract.
    /// </summary>
    public Task<JsonElement?> GetAreasAsync(string userId);

    /// <summary>
    /// Replaces the active profile's whole area list with <paramref name="areas"/> via
    /// <c>POST /api/v2/humans/{id}/areas</c> with <c>trusted: true</c>.
    /// </summary>
    /// <returns>The names PoracleNG left out (its <c>rejected</c> list); empty when it kept everything.</returns>
    public Task<IReadOnlyList<string>> SetAreasTrustedAsync(string userId, IReadOnlyCollection<string> areas);

    /// <summary>
    /// The active profile's current area list, lowercased, from the v2 human read. Throws when it cannot
    /// be read (including a missing human); there is no null and no fallback.
    /// </summary>
    public Task<IReadOnlyList<string>> GetAreaListAsync(string userId);

    /// <summary>The fences <c>GET /api/v2/humans/{id}/areas</c> lists for this human.</summary>
    public Task<IReadOnlyList<V2AreaInfo>> GetAvailableAreasAsync(string userId);

    /// <summary>
    /// Removes <paramref name="name"/> from every profile of the human (<c>DELETE .../areas?all_profiles=true</c>).
    /// With <paramref name="dryRun"/> nothing is written and the result says what would happen.
    /// </summary>
    public Task<AreaRemovalResult> RemoveAreaEverywhereAsync(string userId, string name, bool dryRun);

    /// <summary>
    /// Renames <paramref name="from"/> to <paramref name="to"/> in every profile
    /// (<c>PATCH .../areas?all_profiles=true&amp;trusted=true</c>). <paramref name="to"/> must already be loaded
    /// in PoracleNG.
    /// </summary>
    public Task<AreaRenameResult> RenameAreaEverywhereAsync(string userId, string from, string to, bool dryRun);

    /// <summary>
    /// Switches the user's active profile. PoracleNG handles the area save/load
    /// dual-write atomically.
    /// Maps to POST /api/v2/humans/{userId}/profile
    /// </summary>
    public Task SwitchProfileAsync(string userId, int profileNo);

    /// <summary>
    /// Fetches all profiles for a user in v1's shape: <c>{"profile":[...]}</c> with <c>area</c> and
    /// <c>active_hours</c> as JSON-encoded strings.
    /// Maps to GET /api/v2/humans/{userId}/profiles.
    /// </summary>
    public Task<JsonElement> GetProfilesAsync(string userId);

    /// <summary>
    /// Creates a new profile and returns the number PoracleNG gave it.
    /// Maps to POST /api/v2/humans/{userId}/profiles.
    /// </summary>
    /// <returns>
    /// The assigned profile number. PoracleNG picks the lowest free one, so it cannot be predicted.
    /// </returns>
    /// <exception cref="PoracleRequestRefusedException">
    /// PoracleNG refused the profile, the schedule cannot be said in v2's terms, or the answer carried no
    /// profile number.
    /// </exception>
    public Task<int> AddProfileAsync(string userId, JsonElement body);

    /// <summary>
    /// Updates a profile's name and active hours; <c>profile_no</c> in <paramref name="body"/> says which.
    /// Maps to PATCH /api/v2/humans/{userId}/profiles/{profileNo}.
    /// </summary>
    public Task UpdateProfileAsync(string userId, JsonElement body);

    /// <summary>
    /// Sets one profile's areas and location without touching the active profile, through
    /// <c>POST /api/v2/humans/{userId}/areas?profile=N</c> and <c>POST /api/v2/humans/{userId}/location?profile=N</c>.
    /// Used for a profile just created, duplicated or imported, because PoracleNG's add ignores both. See #993.
    /// </summary>
    /// <remarks>
    /// A non-empty area list is always sent with <c>trusted</c>, because a user's own drawn fences are not
    /// <c>userSelectable</c>; an empty list is sent plain. Names PoracleNG rejects are logged and dropped;
    /// the location is still written.
    /// </remarks>
    /// <exception cref="PoracleRequestRefusedException">PoracleNG refused either write outright.</exception>
    public Task SetProfileGeographyAsync(
        string userId, int profileNo, IReadOnlyCollection<string> areas, double latitude, double longitude);

    /// <summary>
    /// Deletes a profile and its tracking rules; when it was the active one, PoracleNG moves
    /// <c>current_profile_no</c> to another.
    /// Maps to DELETE /api/v2/humans/{userId}/profiles/{profileNo}.
    /// </summary>
    public Task DeleteProfileAsync(string userId, int profileNo);

    /// <summary>
    /// Copies all tracking rules from one profile to another, replacing the destination's.
    /// Maps to POST /api/v2/humans/{userId}/profiles/{toProfileNo}/copy (<c>from_profile</c> in the body).
    /// </summary>
    public Task CopyProfileAsync(string userId, int fromProfileNo, int toProfileNo);

    /// <summary>
    /// The user's saved places, plus the profile pin every alarm falls back to.
    /// Maps to GET /api/v2/humans/{id}/locations
    /// </summary>
    public Task<SavedPlaces> GetPlacesAsync(string userId);

    /// <summary>
    /// Saves a place. PoracleNG reports per-row outcomes rather than failing the request, so a
    /// duplicate label comes back as a message here rather than an exception.
    /// Maps to POST /api/v2/humans/{id}/locations
    /// </summary>
    /// <returns>Null on success, or PoracleNG's reason for refusing this label.</returns>
    public Task<string?> AddPlaceAsync(string userId, SavedPlace place);

    /// <summary>
    /// Moves a saved place, keeping its label so every alarm pointing at it follows.
    /// </summary>
    /// <remarks>
    /// PUT /api/v2/humans/{id}/locations/{label}. Before this route existed, moving a place an alarm
    /// referenced meant repointing every alarm, deleting, re-adding and repointing back, because the
    /// delete answers 409 while anything still points at it.
    /// </remarks>
    /// <exception cref="PoracleUnsupportedException">This PoracleNG is older than 5.4.0 and has no such route.</exception>
    public Task UpdatePlaceAsync(string userId, string label, double latitude, double longitude);

    /// <summary>
    /// Deletes a saved place.
    /// Maps to DELETE /api/v2/humans/{id}/locations/{label}
    /// </summary>
    /// <exception cref="PlaceInUseException">Alarms still point at this place.</exception>
    public Task DeletePlaceAsync(string userId, string label);

    /// <summary>
    /// Admin-bulk human list, optionally filtered by <paramref name="type"/> (e.g. <c>"webhook"</c>) or
    /// restricted to a batch of <paramref name="ids"/>. Maps to <c>GET /api/v2/humans</c>.
    /// </summary>
    /// <remarks>
    /// Never null: nobody matching is an empty list. A PoracleNG without the route (older than 5.4.0)
    /// throws <see cref="PoracleUnsupportedException"/> rather than answering a list that looks genuine.
    /// </remarks>
    public Task<IReadOnlyList<Human>> ListHumansAsync(string? type = null, IReadOnlyCollection<string>? ids = null);

    /// <summary>
    /// Deletes a human and cascades every table PoracleNG owns for it (tracking rules, profiles, summary
    /// schedules) in one call. Maps to <c>DELETE /api/v2/humans/{id}</c>.
    /// </summary>
    /// <returns>
    /// <c>true</c> if deleted; <c>false</c> if PoracleNG answered and the human did not exist. Anything
    /// else throws, including <see cref="PoracleUnsupportedException"/> on a server older than 5.4.0.
    /// </returns>
    public Task<bool> DeleteHumanAsync(string userId);
}
