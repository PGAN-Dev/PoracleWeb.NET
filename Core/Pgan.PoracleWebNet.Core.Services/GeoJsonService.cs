using System.Text.Json;

using Microsoft.Extensions.Logging;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

public partial class GeoJsonService(
    IUserGeofenceService userGeofenceService,
    ILogger<GeoJsonService> logger) : IGeoJsonService
{
    private const int MaxImportSizeBytes = 5 * 1024 * 1024;
    private const int MaxImportFeatures = 50;
    private const int MinPoints = 3;
    private const int MaxPoints = 500;

    private readonly IUserGeofenceService _userGeofenceService = userGeofenceService;
    private readonly ILogger<GeoJsonService> _logger = logger;

    public async Task<GeoJsonFeatureCollection> ExportAsync(string userId)
    {
        var userGeofences = await this._userGeofenceService.GetByUserAsync(userId);

        var collection = new GeoJsonFeatureCollection();

        foreach (var ug in userGeofences)
        {
            if (ug.Polygon is null || ug.Polygon.Length == 0)
            {
                continue;
            }

            var feature = BuildFeature(ug.Polygon, ug.KojiName, ug.GroupName, "user", ug.DisplayName);
            if (feature is not null)
            {
                collection.Features.Add(feature);
            }
        }

        LogExportComplete(this._logger, userId, collection.Features.Count);
        return collection;
    }

    public async Task<GeoJsonImportResult> ImportAsync(string userId, int profileNo, Stream geoJsonStream)
    {
        var result = new GeoJsonImportResult();

        // Read stream with size limit
        using var memoryStream = new MemoryStream();
        var buffer = new byte[8192];
        var totalRead = 0L;
        int bytesRead;

        while ((bytesRead = await geoJsonStream.ReadAsync(buffer)) > 0)
        {
            totalRead += bytesRead;
            if (totalRead > MaxImportSizeBytes)
            {
                throw new InvalidOperationException($"GeoJSON file exceeds the {MaxImportSizeBytes / (1024 * 1024)}MB size limit.");
            }

            await memoryStream.WriteAsync(buffer.AsMemory(0, bytesRead));
        }

        memoryStream.Position = 0;

        // Parse the GeoJSON
        JsonDocument doc;
        try
        {
            doc = await JsonDocument.ParseAsync(memoryStream);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Invalid JSON: {ex.Message}");
        }

        using (doc)
        {
            var features = ExtractFeatures(doc.RootElement, result);

            // Enforce max feature limit
            if (features.Count > MaxImportFeatures)
            {
                result.Errors.Add(new GeoJsonImportError
                {
                    FeatureName = "(collection)",
                    Reason = $"Import limited to {MaxImportFeatures} features. {features.Count - MaxImportFeatures} features were skipped."
                });
                features = [.. features.Take(MaxImportFeatures)];
            }

            var autoNameIndex = 0;

            foreach (var feature in features)
            {
                autoNameIndex++;
                var featureName = ExtractFeatureName(feature, autoNameIndex);

                // Extract geometry
                if (!feature.TryGetProperty("geometry", out var geometry) || geometry.ValueKind == JsonValueKind.Null)
                {
                    result.Errors.Add(new GeoJsonImportError { FeatureName = featureName, Reason = "Feature has no geometry." });
                    continue;
                }

                var geometryType = geometry.TryGetProperty("type", out var gType) ? gType.GetString() ?? string.Empty : string.Empty;

                if (!geometry.TryGetProperty("coordinates", out var coordinates))
                {
                    result.Errors.Add(new GeoJsonImportError { FeatureName = featureName, Reason = "Geometry has no coordinates." });
                    continue;
                }

                // Extract the exterior ring based on geometry type
                double[][]? ring;
                switch (geometryType)
                {
                    case "Polygon":
                        ring = ExtractPolygonRing(coordinates);
                        break;
                    case "MultiPolygon":
                        ring = ExtractMultiPolygonRing(coordinates);
                        break;
                    default:
                        result.Errors.Add(new GeoJsonImportError
                        {
                            FeatureName = featureName,
                            Reason = $"Unsupported geometry type '{geometryType}'. Only Polygon and MultiPolygon are supported."
                        });
                        continue;
                }

                if (ring is null || ring.Length == 0)
                {
                    result.Errors.Add(new GeoJsonImportError { FeatureName = featureName, Reason = "Could not extract coordinates from geometry." });
                    continue;
                }

                // Convert from GeoJSON [lon, lat] to internal [lat, lon]
                var internalRing = ring.Select(coord => new[] { coord[1], coord[0] }).ToArray();

                // Remove closing vertex if last == first
                if (internalRing.Length >= 2 &&
                    Math.Abs(internalRing[0][0] - internalRing[^1][0]) < 1e-9 &&
                    Math.Abs(internalRing[0][1] - internalRing[^1][1]) < 1e-9)
                {
                    internalRing = internalRing[..^1];
                }

                // Validate coordinates
                var invalidCoord = false;
                foreach (var coord in internalRing)
                {
                    if (coord[0] is < -90 or > 90 || coord[1] is < -180 or > 180)
                    {
                        result.Errors.Add(new GeoJsonImportError
                        {
                            FeatureName = featureName,
                            Reason = "Coordinates out of valid range (lat: -90 to 90, lon: -180 to 180)."
                        });
                        invalidCoord = true;
                        break;
                    }
                }

                if (invalidCoord)
                {
                    continue;
                }

                // Validate point count
                if (internalRing.Length < MinPoints)
                {
                    result.Errors.Add(new GeoJsonImportError
                    {
                        FeatureName = featureName,
                        Reason = $"Polygon has {internalRing.Length} points, minimum is {MinPoints}."
                    });
                    continue;
                }

                if (internalRing.Length > MaxPoints)
                {
                    result.Errors.Add(new GeoJsonImportError
                    {
                        FeatureName = featureName,
                        Reason = $"Polygon has {internalRing.Length} points, maximum is {MaxPoints}."
                    });
                    continue;
                }

                // Extract region info from properties if available
                var groupName = string.Empty;
                var parentId = 0;
                if (feature.TryGetProperty("properties", out var featureProps) && featureProps.ValueKind == JsonValueKind.Object)
                {
                    if (featureProps.TryGetProperty("group", out var groupEl) && groupEl.ValueKind == JsonValueKind.String)
                    {
                        groupName = groupEl.GetString() ?? string.Empty;
                    }

                    if (featureProps.TryGetProperty("parentId", out var parentEl) && parentEl.ValueKind == JsonValueKind.Number)
                    {
                        parentId = parentEl.GetInt32();
                    }
                }

                // Create the geofence
                var createModel = new UserGeofenceCreate
                {
                    DisplayName = featureName.Length > 50 ? featureName[..50].TrimEnd() : featureName,
                    GroupName = groupName,
                    ParentId = parentId,
                    Polygon = internalRing
                };

                try
                {
                    var created = await this._userGeofenceService.CreateAsync(userId, profileNo, createModel);
                    result.Created.Add(created);
                }
                catch (InvalidOperationException ex)
                {
                    result.Errors.Add(new GeoJsonImportError { FeatureName = featureName, Reason = ex.Message });
                }
            }
        }

        LogImportComplete(this._logger, userId, result.Created.Count, result.Errors.Count);
        return result;
    }

    private static GeoJsonFeature? BuildFeature(double[][] path, string name, string group, string source, string displayName)
    {
        if (path.Length == 0)
        {
            return null;
        }

        // Convert [lat, lon] to GeoJSON [lon, lat]
        var ring = path.Select(p => new[] { p[1], p[0] }).ToList();

        // Close the ring if not already closed (RFC 7946)
        if (ring.Count >= 2 &&
            (Math.Abs(ring[0][0] - ring[^1][0]) > 1e-9 || Math.Abs(ring[0][1] - ring[^1][1]) > 1e-9))
        {
            ring.Add([ring[0][0], ring[0][1]]);
        }

        var ringArray = ring.ToArray();
        var coordinates = JsonSerializer.SerializeToElement(new[] { ringArray });

        var properties = new Dictionary<string, JsonElement>
        {
            ["name"] = JsonSerializer.SerializeToElement(name),
            ["group"] = JsonSerializer.SerializeToElement(group),
            ["source"] = JsonSerializer.SerializeToElement(source),
            ["displayName"] = JsonSerializer.SerializeToElement(displayName)
        };

        return new GeoJsonFeature
        {
            Geometry = new GeoJsonGeometry { Type = "Polygon", Coordinates = coordinates },
            Properties = properties
        };
    }

    private static List<JsonElement> ExtractFeatures(JsonElement root, GeoJsonImportResult result)
    {
        var features = new List<JsonElement>();

        var type = root.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : null;

        switch (type)
        {
            case "FeatureCollection":
                if (root.TryGetProperty("features", out var featuresArray) && featuresArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var f in featuresArray.EnumerateArray())
                    {
                        features.Add(f);
                    }
                }

                break;
            case "Feature":
                features.Add(root);
                break;
            default:
                result.Errors.Add(new GeoJsonImportError
                {
                    FeatureName = "(root)",
                    Reason = $"Unsupported GeoJSON type '{type ?? "null"}'. Expected 'FeatureCollection' or 'Feature'."
                });
                break;
        }

        return features;
    }

    private static string ExtractFeatureName(JsonElement feature, int autoIndex)
    {
        if (feature.TryGetProperty("properties", out var props) && props.ValueKind == JsonValueKind.Object)
        {
            if (props.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
            {
                var val = name.GetString();
                if (!string.IsNullOrWhiteSpace(val))
                {
                    return val;
                }
            }

            if (props.TryGetProperty("__name", out var dunderName) && dunderName.ValueKind == JsonValueKind.String)
            {
                var val = dunderName.GetString();
                if (!string.IsNullOrWhiteSpace(val))
                {
                    return val;
                }
            }
        }

        return $"Imported {autoIndex}";
    }

    private static double[][]? ExtractPolygonRing(JsonElement coordinates)
    {
        // Polygon coordinates: [ [ [lon,lat], [lon,lat], ... ] ]
        if (coordinates.ValueKind != JsonValueKind.Array || coordinates.GetArrayLength() == 0)
        {
            return null;
        }

        var exteriorRing = coordinates[0];
        return ParseRing(exteriorRing);
    }

    private static double[][]? ExtractMultiPolygonRing(JsonElement coordinates)
    {
        // MultiPolygon coordinates: [ [ [ [lon,lat], ... ] ] ]
        if (coordinates.ValueKind != JsonValueKind.Array || coordinates.GetArrayLength() == 0)
        {
            return null;
        }

        var firstPolygon = coordinates[0];
        if (firstPolygon.ValueKind != JsonValueKind.Array || firstPolygon.GetArrayLength() == 0)
        {
            return null;
        }

        var exteriorRing = firstPolygon[0];
        return ParseRing(exteriorRing);
    }

    private static double[][]? ParseRing(JsonElement ringElement)
    {
        if (ringElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var points = new List<double[]>();
        foreach (var point in ringElement.EnumerateArray())
        {
            if (point.ValueKind != JsonValueKind.Array || point.GetArrayLength() < 2)
            {
                continue;
            }

            var lon = point[0].GetDouble();
            var lat = point[1].GetDouble();
            points.Add([lon, lat]); // Keep as [lon, lat] here; caller swaps
        }

        return points.Count > 0 ? [.. points] : null;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "GeoJSON export for user {UserId}: {FeatureCount} features")]
    private static partial void LogExportComplete(ILogger logger, string userId, int featureCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "GeoJSON import for user {UserId}: {CreatedCount} created, {ErrorCount} errors")]
    private static partial void LogImportComplete(ILogger logger, string userId, int createdCount, int errorCount);
}
