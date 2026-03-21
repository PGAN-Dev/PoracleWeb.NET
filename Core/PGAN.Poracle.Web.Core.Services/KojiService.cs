using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PGAN.Poracle.Web.Core.Abstractions.Services;
using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Core.Services;

public class KojiService(HttpClient httpClient, IConfiguration configuration, ILogger<KojiService> logger) : IKojiService
{
    private static readonly JsonSerializerOptions s_jsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly HttpClient _httpClient = httpClient;
    private readonly string _apiAddress = (configuration["Koji:ApiAddress"] ?? string.Empty).TrimEnd('/');
    private readonly int _projectId = int.TryParse(configuration["Koji:ProjectId"], out var id) ? id : 0;
    private readonly ILogger<KojiService> _logger = logger;

    public async Task SaveGeofenceAsync(string geofenceName, string displayName, string group, int parentId, double[][] polygon, bool isPublic = false)
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
                            ["parent"] = group,
                            ["userSelectable"] = isPublic,
                            ["displayInMatches"] = isPublic
                        }
                    }
                }
            }
        };

        var json = JsonSerializer.Serialize(body, s_jsonOptions);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var response = await this._httpClient.PostAsync($"{this._apiAddress}/api/v1/geofence/save-koji", content);
        response.EnsureSuccessStatusCode();

        this._logger.LogInformation("Saved geofence '{GeofenceName}' to Koji project {ProjectId}", geofenceName, this._projectId);
    }

    public async Task RemoveGeofenceFromProjectAsync(string geofenceName)
    {
        // Fetch existing polygon from Koji
        var polygon = await this.GetGeofencePolygonAsync(geofenceName);
        if (polygon == null || polygon.Length < 3)
        {
            this._logger.LogWarning("Cannot remove geofence '{GeofenceName}': not found or invalid polygon", geofenceName);
            return;
        }

        // Convert [lat,lon] back to GeoJSON [lon,lat]
        var coordinates = polygon.Select(p => new[] { p[1], p[0] }).ToArray();

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
                            ["__projects"] = Array.Empty<int>()
                        }
                    }
                }
            }
        };

        var json = JsonSerializer.Serialize(body, s_jsonOptions);
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
            var regionEntries = entries.Where(e => parentIds.Contains(e.Id)).ToList();

            // Fetch friendly display names from Koji custom properties in parallel
            var fetchTasks = regionEntries.Select(async entry =>
            {
                var displayName = entry.Name;
                double[][]? polygon = null;
                try
                {
                    var featureJson = await this._httpClient.GetStringAsync(
                        $"{this._apiAddress}/api/v1/geofence/area/{Uri.EscapeDataString(entry.Name)}?rt=feature");
                    using var featureDoc = JsonDocument.Parse(featureJson);
                    var feature = featureDoc.RootElement.GetProperty("data");
                    if (feature.TryGetProperty("properties", out var props) &&
                        props.TryGetProperty("name", out var nameEl))
                    {
                        displayName = nameEl.GetString() ?? entry.Name;
                    }

                    // Extract polygon for region detection
                    if (feature.TryGetProperty("geometry", out var geometry) &&
                        geometry.TryGetProperty("coordinates", out var coordinates) &&
                        coordinates.GetArrayLength() > 0)
                    {
                        var ring = coordinates[0];
                        polygon = new double[ring.GetArrayLength()][];
                        for (int i = 0; i < ring.GetArrayLength(); i++)
                        {
                            var coord = ring[i];
                            polygon[i] = [coord[1].GetDouble(), coord[0].GetDouble()];
                        }
                    }
                }
                catch
                {
                    // Fall back to internal name, no polygon
                }

                return new GeofenceRegion
                {
                    Id = entry.Id,
                    Name = entry.Name,
                    DisplayName = displayName,
                    Polygon = polygon
                };
            });

            regions.AddRange(await Task.WhenAll(fetchTasks));
        }

        return regions;
    }

    public async Task<List<UserGeofence>> GetUserGeofencesAsync(string humanId)
    {
        var prefix = $"pweb_{humanId}_";

        // Step 1: Get reference data to find user's geofences by name prefix
        var referenceJson = await this._httpClient.GetStringAsync($"{this._apiAddress}/api/v1/geofence/reference");
        using var referenceDoc = JsonDocument.Parse(referenceJson);

        var userGeofences = new List<UserGeofence>();

        if (!referenceDoc.RootElement.TryGetProperty("data", out var allRefs) || allRefs.ValueKind != JsonValueKind.Array)
        {
            return userGeofences;
        }

        foreach (var item in allRefs.EnumerateArray())
        {
            var name = item.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : "";
            if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var kojiId = item.TryGetProperty("id", out var idProp) ? idProp.GetInt32() : 0;
            var parentId = item.TryGetProperty("parent", out var parentEl) && parentEl.ValueKind == JsonValueKind.Number
                ? parentEl.GetInt32() : 0;

            // Step 2: Get full feature with geometry and custom properties
            try
            {
                var featureJson = await this._httpClient.GetStringAsync(
                    $"{this._apiAddress}/api/v1/geofence/area/{Uri.EscapeDataString(name)}?rt=feature");
                using var featureDoc = JsonDocument.Parse(featureJson);
                var feature = featureDoc.RootElement.GetProperty("data");

                // Extract custom properties
                var props = feature.GetProperty("properties");
                var displayName = props.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? name : name;
                var groupName = props.TryGetProperty("group", out var groupEl) ? groupEl.GetString() ?? "" : "";

                // Extract polygon coordinates (GeoJSON format: [[[lon,lat],...]])
                var geometry = feature.GetProperty("geometry");
                var coordinates = geometry.GetProperty("coordinates");
                var ring = coordinates[0]; // First ring of polygon
                var polygon = new double[ring.GetArrayLength()][];
                for (int i = 0; i < ring.GetArrayLength(); i++)
                {
                    var coord = ring[i];
                    // GeoJSON is [lon, lat], convert to [lat, lon] for frontend
                    polygon[i] = [coord[1].GetDouble(), coord[0].GetDouble()];
                }

                userGeofences.Add(new UserGeofence
                {
                    KojiName = name,
                    DisplayName = displayName,
                    GroupName = groupName,
                    ParentId = parentId,
                    Polygon = polygon,
                });
            }
            catch (Exception ex)
            {
                this._logger.LogWarning(ex, "Failed to fetch feature details for geofence '{GeofenceName}'", name);
            }
        }

        return userGeofences;
    }

    public async Task PromoteGeofenceAsync(string currentName, string? newName, string displayName, string group, int parentId)
    {
        // Fetch existing polygon from Koji
        var polygon = await this.GetGeofencePolygonAsync(currentName)
            ?? throw new InvalidOperationException($"Geofence '{currentName}' not found in Koji");

        // Convert [lat,lon] back to GeoJSON [lon,lat]
        var geoJsonCoords = polygon.Select(p => new[] { p[1], p[0] }).ToArray();

        var targetName = newName ?? currentName;

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
                            coordinates = new[] { geoJsonCoords }
                        },
                        properties = new Dictionary<string, object>
                        {
                            ["__name"] = targetName,
                            ["__mode"] = "unset",
                            ["__projects"] = new[] { this._projectId },
                            ["__parent"] = parentId,
                            ["userSelectable"] = true,
                            ["displayInMatches"] = false,
                            ["name"] = displayName,
                            ["group"] = group,
                            ["parent"] = group
                        }
                    }
                }
            }
        };

        var json = JsonSerializer.Serialize(body, s_jsonOptions);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var response = await this._httpClient.PostAsync($"{this._apiAddress}/api/v1/geofence/save-koji", content);
        response.EnsureSuccessStatusCode();

        // If renaming, remove the old geofence from the project
        if (newName != null && !string.Equals(newName, currentName, StringComparison.Ordinal))
        {
            await this.RemoveGeofenceFromProjectAsync(currentName);
        }

        this._logger.LogInformation(
            "Promoted geofence '{CurrentName}' as '{TargetName}' with display name '{DisplayName}' in group '{Group}'",
            currentName, targetName, displayName, group);
    }

    public async Task<double[][]?> GetGeofencePolygonAsync(string geofenceName)
    {
        try
        {
            var featureJson = await this._httpClient.GetStringAsync(
                $"{this._apiAddress}/api/v1/geofence/area/{Uri.EscapeDataString(geofenceName)}?rt=feature");
            using var featureDoc = JsonDocument.Parse(featureJson);
            var feature = featureDoc.RootElement.GetProperty("data");
            var geometry = feature.GetProperty("geometry");
            var coordinates = geometry.GetProperty("coordinates");
            var ring = coordinates[0]; // First ring of polygon

            var polygon = new double[ring.GetArrayLength()][];
            for (int i = 0; i < ring.GetArrayLength(); i++)
            {
                var coord = ring[i];
                // GeoJSON is [lon, lat], convert to [lat, lon]
                polygon[i] = [coord[1].GetDouble(), coord[0].GetDouble()];
            }

            return polygon;
        }
        catch (HttpRequestException ex)
        {
            this._logger.LogWarning(ex, "Geofence '{GeofenceName}' not found in Koji", geofenceName);
            return null;
        }
    }
}
