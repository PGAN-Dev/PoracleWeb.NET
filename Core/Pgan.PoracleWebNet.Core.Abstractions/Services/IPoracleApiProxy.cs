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
