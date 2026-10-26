using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

public interface IPoracleApiProxy
{
    Task<PoracleConfig?> GetConfigAsync(CancellationToken cancellationToken = default);
    Task<bool?> GetQuestSummaryEnabledAsync();

    /// <summary>
    /// Reads <c>general.disable_showcase</c> from PoracleNG's config-values endpoint. It is enforced
    /// upstream but omitted from <c>disabledHooks</c>.
    /// </summary>
    /// <remarks>
    /// <c>null</c> here means PoracleNG could not be read, or did not report the key. Every supported
    /// PoracleNG (5.4.0 and later) has the <c>/api/v2/.../tracking/incident</c> route.
    /// </remarks>
    Task<bool?> GetShowcaseDisabledAsync();

    /// <summary>
    /// Reads <c>area_security.enabled</c> from PoracleNG's config-values endpoint. Used only to decide
    /// whether it is safe to rely on <c>trusted</c> on <c>setAreas</c> for the community-restriction
    /// question jfberry/PoracleNG#228/#230 cover — see #838. Returns <c>null</c> when the value cannot
    /// be determined (older Poracle, PoracleJS, endpoint shape changed); callers must treat that as
    /// "cannot confirm disabled", not as "disabled".
    /// </summary>
    Task<bool?> GetAreaSecurityEnabledAsync();

    /// <summary>
    /// The Telegram groups PoracleNG registers people from, read from <c>GET /api/config/values</c>:
    /// <c>telegram.channels</c>, <c>area_security.enabled</c> and each <c>area_security.communities</c> row's
    /// <c>telegram_channels</c>. Absent sections read as empty. A non-success answer throws, so a caller can tell
    /// "nobody can register" from "could not read".
    /// </summary>
    Task<TelegramRegistrationGroups> GetTelegramRegistrationGroupsAsync();

    /// <summary>
    /// Reads <c>tracking.pokemon_edit</c> from PoracleNG's config-values endpoint. The key only exists
    /// on a PoracleNG built after jfberry/PoracleNG#246 (merged 2026-10-08, not yet in a tagged
    /// release): before that, the processor accepted the pokemon <c>edit</c> bit (commands, v1 and v2)
    /// but never acted on it, so the bit was storable and readable but inert. <c>null</c> means the key
    /// is absent -- an older build, or an unreadable response -- distinct from an operator explicitly
    /// setting <c>pokemon_edit = false</c>. Either way the caller must treat it as "cannot offer the
    /// control", not "offer it anyway".
    /// </summary>
    Task<bool?> GetPokemonEditEnabledAsync();

    Task<string?> GetTemplatesAsync();
    Task<string?> GetGruntsAsync(string? locale = null);

    /// <summary>
    /// Localized monster master data: names, types and form names in <paramref name="locale"/>.
    /// PoracleNG translates these from its own i18n bundle, which is why they are fetched from it
    /// rather than from the English-only WatWowMap masterfile.
    /// </summary>
    /// <returns>The raw JSON map keyed <c>"{pokemonId}_{formId}"</c>, or <c>null</c> when upstream
    /// is unreachable or does not serve it.</returns>
    Task<string?> GetMonstersAsync(string locale);
    Task<string?> GetGeofenceAsync();
    Task<string?> GetAreasWithGroupsAsync(string userId);
    Task<string?> GetAreaMapUrlAsync(string areaName);
    Task<string?> GetAllGeofenceDataAsync();
    Task<string?> GetLocationMapUrlAsync(double lat, double lon);
    Task<string?> GetDistanceMapUrlAsync(double lat, double lon, int distance);
    Task ReloadGeofencesAsync();
    Task SendTestAlertAsync(TestAlertRequest request);
    Task<string?> GetGeofencesGeoJsonAsync();

    /// <summary>
    /// Forward geocode through PoracleNG's own <c>/api/geocode/forward</c>, which resolves via whichever
    /// provider the operator configured (Nominatim, Photon or Google) and answers in one shape regardless
    /// -- so this app never parses a provider's payload itself. Returns the raw JSON array PoracleNG
    /// answers with (empty when nothing matched), or <c>null</c> when PoracleNG answers non-success:
    /// 503 when no provider is configured, or the request timed out. A plaintext 404 (a PoracleNG below
    /// 5.4.0, which has no such route) throws <see cref="PoracleUnsupportedException"/> rather than
    /// reaching for a provider directly. See #845.
    /// </summary>
    Task<string?> GetGeocodeForwardAsync(string query, string? language = null);

    /// <summary>
    /// Reverse geocode through PoracleNG's own <c>/api/geocode/reverse</c>. Returns the raw JSON object,
    /// or <c>null</c> on the same conditions as <see cref="GetGeocodeForwardAsync"/> -- including a
    /// problem+json 404 (a real "nothing at this coordinate"; the plaintext 404 a missing route answers
    /// with throws <see cref="PoracleUnsupportedException"/> instead).
    /// </summary>
    Task<string?> GetGeocodeReverseAsync(double lat, double lon, string? language = null);
}
