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
    /// <returns>The human, or null when PoracleNG answers 404: it has no such human.</returns>
    /// <exception cref="HttpRequestException">
    /// PoracleNG answered anything else non-2xx, or could not be reached. The caller must not read that
    /// as "this account does not exist". It used to answer null here too, which is how an outage was
    /// reported as a deleted account. See #994.
    /// </exception>
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
    /// POST /api/v2/humans/{userId}/location with a {latitude,longitude} body.
    /// </summary>
    public Task SetLocationAsync(string userId, double lat, double lon);

    /// <summary>
    /// The channels, webhooks and users this human may administer on Discord and Telegram.
    /// </summary>
    /// <remarks>
    /// GET /api/v2/humans/{userId}/admin-roles, or v1's getAdministrationRoles. Both compute the same
    /// answer -- v2's handler calls the same delegated-administration logic -- and the only difference
    /// on the wire is v1's extra "status":"ok".
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
    /// Maps to POST /api/v2/humans/{userId}/areas (never with <c>trusted</c>) where the server's schema says
    /// that answer reports what it refused, or POST /api/humans/{userId}/setAreas.
    /// </summary>
    /// <remarks>
    /// Either way PoracleNG leaves out names this user may not select, including every user-drawn fence.
    /// That is not an error on either surface; the caller re-adds the user's own fences afterwards.
    /// </remarks>
    public Task SetAreasAsync(string userId, string[] areas);

    /// <summary>
    /// Fetches the user's current area subscriptions: the human record, read the same way as
    /// <see cref="GetHumanAsync"/>, with the same null-on-404, throw-otherwise contract.
    /// </summary>
    public Task<JsonElement?> GetAreasAsync(string userId);

    /// <summary>
    /// Adds one area to the human's active profile via <c>POST /api/v2/humans/{id}/areas</c> with
    /// <c>trusted: true</c>, bypassing the <c>userSelectable</c> filter non-admin writes would otherwise
    /// hit -- the same thing <c>IUserAreaDualWriter.AddAreaToActiveProfileAsync</c> does by writing
    /// <c>humans.area</c>/<c>profiles.area</c> directly. See #838.
    /// </summary>
    /// <returns>
    /// <c>null</c> when this is not safe to rely on yet (the server's schema lacks <c>trusted</c>, or
    /// area_security is enabled and this server cannot be confirmed to carry jfberry/PoracleNG#230's
    /// community-restriction fix), or PoracleNG rejected a requested name (it does not know that fence)
    /// -- the caller must fall back to <c>IUserAreaDualWriter</c>. <c>false</c>
    /// when the area was already present. <c>true</c> once written.
    /// </returns>
    public Task<bool?> AddAreaToActiveProfileTrustedAsync(string userId, string areaName);

    /// <summary>Bulk form of <see cref="AddAreaToActiveProfileTrustedAsync"/>.</summary>
    public Task<bool?> AddAreasToActiveProfileTrustedAsync(string userId, IReadOnlyCollection<string> areaNames);

    /// <summary>
    /// Removes one area from the human's active profile via the same trusted <c>setAreas</c> call. See
    /// <see cref="AddAreaToActiveProfileTrustedAsync"/> for the null/false/true contract.
    /// </summary>
    public Task<bool?> RemoveAreaFromActiveProfileTrustedAsync(string userId, string areaName);

    /// <summary>
    /// Switches the user's active profile. PoracleNG handles the area save/load
    /// dual-write atomically.
    /// Maps to POST /api/humans/{userId}/switchProfile/{profileNo}
    /// </summary>
    public Task SwitchProfileAsync(string userId, int profileNo);

    /// <summary>
    /// Fetches all profiles for a user, always in v1's shape: <c>{"profile":[...]}</c> with <c>area</c> and
    /// <c>active_hours</c> as JSON-encoded strings.
    /// Maps to GET /api/v2/humans/{userId}/profiles where declared, or GET /api/profiles/{userId}.
    /// </summary>
    public Task<JsonElement> GetProfilesAsync(string userId);

    /// <summary>
    /// Creates a new profile, and reports the number it was given when the server says so.
    /// Maps to POST /api/v2/humans/{userId}/profiles, or POST /api/profiles/{userId}/add.
    /// </summary>
    /// <returns>
    /// The assigned profile number, or <c>null</c> when this server does not report it — which is every
    /// released PoracleNG, so callers still need <c>ProfileNumbering.ResolveCreated</c> as a fallback.
    /// PoracleNG picks the lowest free number, so <c>null</c> cannot be replaced by arithmetic.
    /// </returns>
    public Task<int?> AddProfileAsync(string userId, JsonElement body);

    /// <summary>
    /// Updates a profile's name and active hours.
    /// Maps to PATCH /api/v2/humans/{userId}/profiles/{profileNo}, or POST /api/profiles/{userId}/update.
    /// </summary>
    /// <returns>
    /// <c>true</c> when the server applied the name as well. Released PoracleNG accepts the request and
    /// silently keeps the old name, so a <c>false</c> here means the caller must still write
    /// <c>profiles.name</c> itself.
    /// </returns>
    public Task<bool> UpdateProfileAsync(string userId, JsonElement body);

    /// <summary>
    /// Sets one profile's areas and location without touching the active profile, through
    /// <c>POST /api/v2/humans/{userId}/areas?profile=N</c> and <c>POST /api/v2/humans/{userId}/location?profile=N</c>.
    /// Used for a profile just created, duplicated or imported, because PoracleNG's add ignores both. See #993.
    /// </summary>
    /// <remarks>
    /// A non-empty area list is sent with <c>trusted</c>, because a user's own drawn fences are not
    /// <c>userSelectable</c>; that is only done where <c>trusted</c> is safe to rely on, the same gate as
    /// <see cref="AddAreaToActiveProfileTrustedAsync"/>. An empty list needs no trust and is always sent plain.
    /// </remarks>
    /// <returns>
    /// <c>true</c> once both are written. <c>false</c> when the caller must write them itself: the server does
    /// not declare the two routes with <c>?profile=</c>, <c>trusted</c> is needed and not safe, a route is
    /// missing, or PoracleNG refused a requested area name (one it has not loaded) -- the same "fall back"
    /// rule the trusted active-profile writes follow.
    /// </returns>
    /// <exception cref="PoracleRequestRefusedException">PoracleNG refused either write outright.</exception>
    public Task<bool> SetProfileGeographyAsync(
        string userId, int profileNo, IReadOnlyCollection<string> areas, double latitude, double longitude);

    /// <summary>
    /// Deletes a profile and its tracking rules; when it was the active one, PoracleNG moves
    /// <c>current_profile_no</c> to another.
    /// Maps to DELETE /api/v2/humans/{userId}/profiles/{profileNo} where declared, or
    /// DELETE /api/profiles/{userId}/byProfileNo/{profileNo}.
    /// </summary>
    public Task DeleteProfileAsync(string userId, int profileNo);

    /// <summary>
    /// Copies all tracking rules from one profile to another, replacing the destination's.
    /// Maps to POST /api/v2/humans/{userId}/profiles/{toProfileNo}/copy (<c>from_profile</c> in the body)
    /// where declared, or POST /api/profiles/{userId}/copy/{fromProfileNo}/{toProfileNo}.
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
    /// <exception cref="PoracleUnsupportedException">This PoracleNG is older than 5.4.0 and has no such route.</exception>
    public Task UpdatePlaceAsync(string userId, string label, double latitude, double longitude);

    /// <summary>
    /// Deletes a saved place.
    /// Maps to POST /api/humans/{id}/locations/{label}/delete
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
