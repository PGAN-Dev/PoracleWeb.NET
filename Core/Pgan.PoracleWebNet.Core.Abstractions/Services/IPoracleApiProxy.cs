using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

public interface IPoracleApiProxy
{
    public Task<PoracleConfig?> GetConfigAsync();
    public Task<string?> GetAreasAsync(string userId);
    public Task<string?> GetTemplatesAsync();
    public Task<string?> GetAdminRolesAsync(string userId);
    public Task<string?> GetGruntsAsync();
    public Task<string?> GetGeofenceAsync();
    public Task<string?> GetAreasWithGroupsAsync(string userId);
    public Task<string?> GetAreaMapUrlAsync(string areaName);
    public Task<string?> GetAllGeofenceDataAsync();
    public Task<string?> GetLocationMapUrlAsync(double lat, double lon);
    public Task<string?> GetDistanceMapUrlAsync(double lat, double lon, int distance);
    public Task ReloadGeofencesAsync();
    public Task SendTestAlertAsync(TestAlertRequest request);
}
