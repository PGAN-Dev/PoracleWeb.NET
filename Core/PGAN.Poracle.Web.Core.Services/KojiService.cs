using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PGAN.Poracle.Web.Core.Abstractions.Services;
using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Core.Services;

public partial class KojiService(HttpClient httpClient, IConfiguration configuration, IMemoryCache memoryCache, ILogger<KojiService> logger) : IKojiService
{
    private const string AdminGeofenceCacheKey = "koji_admin_geofences";
    private static readonly TimeSpan s_cacheDuration = TimeSpan.FromMinutes(5);
    private static readonly JsonSerializerOptions s_jsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly HttpClient _httpClient = httpClient;
    private readonly IMemoryCache _memoryCache = memoryCache;
    private readonly string _apiAddress = (configuration["Koji:ApiAddress"] ?? string.Empty).TrimEnd('/');
    private readonly int _projectId = int.TryParse(configuration["Koji:ProjectId"], out var id) ? id : 0;
    private readonly string _projectName = configuration["Koji:ProjectName"] ?? string.Empty;
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

        LogGeofenceSaved(this._logger, geofenceName, this._projectId);
    }

    public async Task RemoveGeofenceFromProjectAsync(string geofenceName)
    {
        // Fetch existing polygon from Koji
        var polygon = await this.GetGeofencePolygonAsync(geofenceName);
        if (polygon == null || polygon.Length < 3)
        {
            LogCannotRemoveGeofence(this._logger, geofenceName);
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

        LogGeofenceRemoved(this._logger, geofenceName);
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
                        for (var i = 0; i < ring.GetArrayLength(); i++)
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
                for (var i = 0; i < ring.GetArrayLength(); i++)
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
                LogFetchFeatureDetailsFailed(this._logger, ex, name);
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

        LogGeofencePromoted(this._logger, currentName, targetName, displayName, group);
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
            for (var i = 0; i < ring.GetArrayLength(); i++)
            {
                var coord = ring[i];
                // GeoJSON is [lon, lat], convert to [lat, lon]
                polygon[i] = [coord[1].GetDouble(), coord[0].GetDouble()];
            }

            return polygon;
        }
        catch (HttpRequestException ex)
        {
            LogGeofenceNotFoundInKoji(this._logger, ex, geofenceName);
            return null;
        }
    }

    public async Task<List<AdminGeofence>> GetAdminGeofencesAsync()
    {
        if (this._memoryCache.TryGetValue(AdminGeofenceCacheKey, out List<AdminGeofence>? cached) && cached != null)
        {
            return cached;
        }

        var result = await this.FetchAdminGeofencesFromKojiAsync();
        this._memoryCache.Set(AdminGeofenceCacheKey, result, s_cacheDuration);
        return result;
    }

    public void InvalidateAdminGeofenceCache()
    {
        this._memoryCache.Remove(AdminGeofenceCacheKey);
        LogAdminGeofenceCacheInvalidated(this._logger);
    }

    private async Task<List<AdminGeofence>> FetchAdminGeofencesFromKojiAsync()
    {
        if (string.IsNullOrEmpty(this._projectName))
        {
            LogProjectNameNotConfigured(this._logger);
            return [];
        }

        // Step 1: Fetch reference data to get parent IDs
        var referenceJson = await this._httpClient.GetStringAsync($"{this._apiAddress}/api/v1/geofence/reference");
        using var referenceDoc = JsonDocument.Parse(referenceJson);

        if (!referenceDoc.RootElement.TryGetProperty("data", out var refData) || refData.ValueKind != JsonValueKind.Array)
        {
            LogReferenceEndpointNoData(this._logger);
            return [];
        }

        // Build id→name map and identify parent IDs
        var idToName = new Dictionary<int, string>();
        var parentIds = new HashSet<int>();

        foreach (var entry in refData.EnumerateArray())
        {
            var id = entry.TryGetProperty("id", out var idProp) ? idProp.GetInt32() : 0;
            var name = entry.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? string.Empty : string.Empty;
            var parent = entry.TryGetProperty("parent", out var parentProp) && parentProp.ValueKind == JsonValueKind.Number
                ? parentProp.GetInt32()
                : (int?)null;

            idToName[id] = name;

            if (parent.HasValue)
            {
                parentIds.Add(parent.Value);
            }
        }

        // Step 2: Fetch friendly display names for parent geofences (these become group names)
        var parentIdToGroupName = new Dictionary<int, string>();
        var parentFetchTasks = parentIds.Select(async parentId =>
        {
            if (!idToName.TryGetValue(parentId, out var parentInternalName))
            {
                return;
            }

            try
            {
                var featureJson = await this._httpClient.GetStringAsync(
                    $"{this._apiAddress}/api/v1/geofence/area/{Uri.EscapeDataString(parentInternalName)}?rt=feature");
                using var featureDoc = JsonDocument.Parse(featureJson);
                var feature = featureDoc.RootElement.GetProperty("data");

                var displayName = parentInternalName;
                if (feature.TryGetProperty("properties", out var props) &&
                    props.TryGetProperty("name", out var nameEl))
                {
                    displayName = nameEl.GetString() ?? parentInternalName;
                }

                lock (parentIdToGroupName)
                {
                    parentIdToGroupName[parentId] = displayName;
                }
            }
            catch (Exception ex)
            {
                LogFetchParentDisplayNameFailed(this._logger, ex, parentInternalName, parentId);
                lock (parentIdToGroupName)
                {
                    parentIdToGroupName[parentId] = parentInternalName;
                }
            }
        });

        await Task.WhenAll(parentFetchTasks);

        // Build parent-to-child map from reference data for filtering
        var childParentMap = new Dictionary<string, int>();
        foreach (var entry in refData.EnumerateArray())
        {
            var name = entry.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? string.Empty : string.Empty;
            var parent = entry.TryGetProperty("parent", out var parentProp) && parentProp.ValueKind == JsonValueKind.Number
                ? parentProp.GetInt32()
                : 0;

            if (parent > 0)
            {
                childParentMap[name] = parent;
            }
        }

        // Step 3: Fetch Poracle format geofences for the project
        var poracleJson = await this._httpClient.GetStringAsync(
            $"{this._apiAddress}/api/v1/geofence/poracle/{Uri.EscapeDataString(this._projectName)}?name=true");
        using var poracleDoc = JsonDocument.Parse(poracleJson);

        if (!poracleDoc.RootElement.TryGetProperty("data", out var poracleData) || poracleData.ValueKind != JsonValueKind.Array)
        {
            LogPoracleEndpointNoData(this._logger, this._projectName);
            return [];
        }

        // Build set of parent names for O(1) exclusion lookup
        var parentNames = new HashSet<string>(
            parentIds.Where(idToName.ContainsKey).Select(pid => idToName[pid]),
            StringComparer.Ordinal);

        var adminGeofences = new List<AdminGeofence>();
        var geofenceId = 0;

        foreach (var item in poracleData.EnumerateArray())
        {
            var name = item.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? string.Empty : string.Empty;

            // Skip parent/region geofences — they're scanner regions, not user-selectable areas
            if (parentNames.Contains(name))
            {
                continue;
            }

            // Extract path (Koji Poracle format returns path as [[lat,lon],...])
            double[][] path = [];
            if (item.TryGetProperty("path", out var pathEl) && pathEl.ValueKind == JsonValueKind.Array)
            {
                path = new double[pathEl.GetArrayLength()][];
                for (var i = 0; i < pathEl.GetArrayLength(); i++)
                {
                    var point = pathEl[i];
                    if (point.ValueKind == JsonValueKind.Array && point.GetArrayLength() >= 2)
                    {
                        path[i] = [point[0].GetDouble(), point[1].GetDouble()];
                    }
                }
            }

            if (path.Length < 3)
            {
                continue;
            }

            // Resolve group from parent chain
            var group = string.Empty;
            if (childParentMap.TryGetValue(name, out var parentId) &&
                parentIdToGroupName.TryGetValue(parentId, out var groupName))
            {
                group = groupName;
            }

            // Extract description and color from Koji properties if available
            var description = item.TryGetProperty("description", out var descEl) ? descEl.GetString() ?? string.Empty : string.Empty;
            var color = item.TryGetProperty("color", out var colorEl) ? colorEl.GetString() ?? "#3399ff" : "#3399ff";

            adminGeofences.Add(new AdminGeofence
            {
                Id = ++geofenceId,
                Name = name,
                Group = group,
                Path = path,
                UserSelectable = true,
                DisplayInMatches = true,
                Description = description,
                Color = color,
            });
        }

        LogAdminGeofencesFetched(this._logger, adminGeofences.Count, this._projectName, parentIdToGroupName.Count);

        return adminGeofences;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Fetched {Count} admin geofences from Koji project '{ProjectName}' with {ParentCount} groups resolved")]
    private static partial void LogAdminGeofencesFetched(ILogger logger, int count, string projectName, int parentCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Saved geofence '{GeofenceName}' to Koji project {ProjectId}")]
    private static partial void LogGeofenceSaved(ILogger logger, string geofenceName, int projectId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cannot remove geofence '{GeofenceName}': not found or invalid polygon")]
    private static partial void LogCannotRemoveGeofence(ILogger logger, string geofenceName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Removed geofence '{GeofenceName}' from Koji project")]
    private static partial void LogGeofenceRemoved(ILogger logger, string geofenceName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to fetch feature details for geofence '{GeofenceName}'")]
    private static partial void LogFetchFeatureDetailsFailed(ILogger logger, Exception ex, string geofenceName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Promoted geofence '{CurrentName}' as '{TargetName}' with display name '{DisplayName}' in group '{Group}'")]
    private static partial void LogGeofencePromoted(ILogger logger, string currentName, string targetName, string displayName, string group);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Geofence '{GeofenceName}' not found in Koji")]
    private static partial void LogGeofenceNotFoundInKoji(ILogger logger, Exception ex, string geofenceName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Admin geofence cache invalidated")]
    private static partial void LogAdminGeofenceCacheInvalidated(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Koji:ProjectName is not configured — cannot fetch admin geofences")]
    private static partial void LogProjectNameNotConfigured(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Koji reference endpoint returned no data array")]
    private static partial void LogReferenceEndpointNoData(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to fetch parent geofence display name for '{ParentName}' (ID {ParentId})")]
    private static partial void LogFetchParentDisplayNameFailed(ILogger logger, Exception ex, string parentName, int parentId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Koji Poracle endpoint returned no data array for project '{ProjectName}'")]
    private static partial void LogPoracleEndpointNoData(ILogger logger, string projectName);
}
