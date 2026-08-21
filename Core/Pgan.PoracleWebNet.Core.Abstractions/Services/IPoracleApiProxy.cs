using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

public interface IPoracleApiProxy
{
    Task<PoracleConfig?> GetConfigAsync();
    Task<bool?> GetQuestSummaryEnabledAsync();
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
