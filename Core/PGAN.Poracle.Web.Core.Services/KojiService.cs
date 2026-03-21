using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PGAN.Poracle.Web.Core.Abstractions.Services;
using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Core.Services;

public class KojiService(HttpClient httpClient, IConfiguration configuration, ILogger<KojiService> logger) : IKojiService
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly string _apiAddress = (configuration["Koji:ApiAddress"] ?? string.Empty).TrimEnd('/');
    private readonly int _projectId = int.TryParse(configuration["Koji:ProjectId"], out var id) ? id : 0;
    private readonly ILogger<KojiService> _logger = logger;

    public async Task SaveGeofenceAsync(string geofenceName, string displayName, string group, int parentId, double[][] polygon)
    {
        var coordinates = polygon.Select(p => new[] { p[1], p[0] }).ToArray(); // Convert lat,lon to GeoJSON lon,lat

        var body = new
        {
            area = new
            {
                type = "FeatureCollection",
                features = new[]
                {
                    new
                    {
                        type = "Feature",
                        geometry = new
                        {
                            type = "Polygon",
                            coordinates = new[] { coordinates }
                        },
                        properties = new Dictionary<string, object>
                        {
                            ["__name"] = geofenceName,
                            ["__mode"] = "unset",
                            ["__projects"] = new[] { this._projectId },
                            ["__parent"] = parentId,
                            ["name"] = displayName,
                            ["group"] = group,
                            ["parent"] = group
                        }
                    }
                }
            }
        };

        var json = JsonSerializer.Serialize(body, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var response = await this._httpClient.PostAsync($"{this._apiAddress}/api/v1/geofence/save-koji", content);
        response.EnsureSuccessStatusCode();

        this._logger.LogInformation("Saved geofence '{GeofenceName}' to Koji project {ProjectId}", geofenceName, this._projectId);
    }

    public async Task RemoveGeofenceFromProjectAsync(string geofenceName, double[][] polygon)
    {
        var coordinates = polygon.Select(p => new[] { p[1], p[0] }).ToArray(); // Convert lat,lon to GeoJSON lon,lat

        var body = new
        {
            area = new
            {
                type = "FeatureCollection",
                features = new[]
                {
                    new
                    {
                        type = "Feature",
                        geometry = new
                        {
                            type = "Polygon",
                            coordinates = new[] { coordinates }
                        },
                        properties = new Dictionary<string, object>
                        {
                            ["__name"] = geofenceName,
                            ["__mode"] = "unset",
                            ["__projects"] = Array.Empty<int>(),
                            ["__parent"] = 0
                        }
                    }
                }
            }
        };

        var json = JsonSerializer.Serialize(body, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var response = await this._httpClient.PostAsync($"{this._apiAddress}/api/v1/geofence/save-koji", content);
        response.EnsureSuccessStatusCode();

        this._logger.LogInformation("Removed geofence '{GeofenceName}' from Koji project", geofenceName);
    }

    public async Task<List<GeofenceRegion>> GetRegionsAsync()
    {
        var response = await this._httpClient.GetAsync($"{this._apiAddress}/api/v1/geofence/reference");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        var regions = new List<GeofenceRegion>();

        // Collect all parent IDs referenced by geofences
        var parentIds = new HashSet<int>();
        var entries = new List<(int Id, string Name)>();

        if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in data.EnumerateArray())
            {
                var id = entry.TryGetProperty("id", out var idProp) ? idProp.GetInt32() : 0;
                var name = entry.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? string.Empty : string.Empty;
                var parent = entry.TryGetProperty("parent", out var parentProp) && parentProp.ValueKind == JsonValueKind.Number
                    ? parentProp.GetInt32()
                    : (int?)null;

                entries.Add((id, name));

                if (parent.HasValue)
                {
                    parentIds.Add(parent.Value);
                }
            }

            // Regions are entries that are used as parents by other geofences
            foreach (var (id, name) in entries)
            {
                if (parentIds.Contains(id))
                {
                    regions.Add(new GeofenceRegion
                    {
                        Id = id,
                        Name = name,
                        DisplayName = name
                    });
                }
            }
        }

        return regions;
    }
}
