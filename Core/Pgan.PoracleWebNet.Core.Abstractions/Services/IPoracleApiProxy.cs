using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

public interface IPoracleApiProxy
{
    Task<PoracleConfig?> GetConfigAsync();
    Task<bool?> GetQuestSummaryEnabledAsync();

    /// <summary>
    /// Reads <c>general.disable_fort_update</c> from PoracleNG's config-values endpoint. PoracleNG
    /// honours this flag in the processor and the bot but leaves it out of the <c>disabledHooks</c>
    /// array on <c>/api/config/poracleWeb</c>, so fort changes have to be asked about separately.
    /// Returns <c>null</c> when the value cannot be determined (older Poracle, PoracleJS, endpoint
    /// shape changed) so the caller can leave the site setting in sole charge.
    /// </summary>
    Task<bool?> GetFortUpdateDisabledAsync();
    Task<string?> GetAreasAsync(string userId);
    Task<string?> GetTemplatesAsync();
    Task<string?> GetAdminRolesAsync(string userId);
    Task<string?> GetGruntsAsync();

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
}
