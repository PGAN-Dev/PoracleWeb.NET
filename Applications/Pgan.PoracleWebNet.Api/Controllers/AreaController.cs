using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Pgan.PoracleWebNet.Core.Abstractions.Services;

namespace Pgan.PoracleWebNet.Api.Controllers;

[Route("api/areas")]
public partial class AreaController(IPoracleHumanProxy humanProxy, IPoracleApiProxy poracleApiProxy, ILogger<AreaController> logger) : BaseApiController
{
    private readonly IPoracleHumanProxy _humanProxy = humanProxy;
    private readonly IPoracleApiProxy _poracleApiProxy = poracleApiProxy;
    private readonly ILogger<AreaController> _logger = logger;

    [HttpGet]
    public async Task<IActionResult> GetSelectedAreas()
    {
        // Read areas from PoracleNG, which returns the current profile's areas
        var humanJson = await this._humanProxy.GetAreasAsync(this.UserId);
        if (humanJson == null)
        {
            return this.NotFound();
        }

        // The proxy returns a JsonElement for the human; extract the area field
        if (humanJson.Value.TryGetProperty("area", out var areaProp))
        {
            return this.Ok(ParseAreaJson(areaProp.GetString()));
        }

        return this.Ok(Array.Empty<string>());
    }

    [HttpGet("available")]
    public async Task<IActionResult> GetAvailableAreas()
    {
        try
        {
            var areasJson = await this._poracleApiProxy.GetAreasWithGroupsAsync(this.UserId);
            if (areasJson != null)
            {
                return this.Content(areasJson, "application/json");
            }
        }
        catch (Exception ex)
        {
            LogFetchAvailableAreasFailed(this._logger, ex, this.UserId);
        }

        return this.Ok(Array.Empty<object>());
    }

    [HttpPut]
    public async Task<IActionResult> UpdateAreas([FromBody] UpdateAreasRequest request)
    {
        // Lowercase area names to match Poracle's expected format (PHP PoracleWeb does strtolower)
        var normalizedAreas = request.Areas != null && request.Areas.Length > 0
            ? request.Areas.Select(a => a.ToLowerInvariant()).ToArray()
            : [];

        // PoracleNG handles the dual-write to humans.area + profiles.area atomically
        await this._humanProxy.SetAreasAsync(this.UserId, normalizedAreas);

        return this.Ok(normalizedAreas);
    }

    [HttpGet("geofence")]
    public async Task<IActionResult> GetGeofencePolygons()
    {
        try
        {
            var json = await this._poracleApiProxy.GetAllGeofenceDataAsync();
            if (json != null)
            {
                return this.Content(json, "application/json");
            }
        }
        catch (Exception ex)
        {
            LogFetchGeofenceDataFailed(this._logger, ex);
        }

        return this.Ok(new
        {
            status = "ok",
            geofence = Array.Empty<object>()
        });
    }

    [HttpGet("map/{areaName}")]
    public async Task<IActionResult> GetAreaMap(string areaName)
    {
        try
        {
            var mapUrl = await this._poracleApiProxy.GetAreaMapUrlAsync(Uri.UnescapeDataString(areaName));
            if (mapUrl != null)
            {
                return this.Ok(new
                {
                    url = mapUrl
                });
            }
        }
        catch (Exception ex)
        {
            LogFetchAreaMapFailed(this._logger, ex, areaName);
        }

        return this.NotFound();
    }

    public class UpdateAreasRequest
    {
        public string[]? Areas
        {
            get; set;
        }
    }

    private static string[] ParseAreaJson(string? areaJson)
    {
        if (string.IsNullOrWhiteSpace(areaJson))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<string[]>(areaJson) ?? [];
        }
        catch
        {
            return areaJson.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to fetch available areas from Poracle API for user {UserId}")]
    private static partial void LogFetchAvailableAreasFailed(ILogger logger, Exception ex, string userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to fetch geofence data from Poracle API")]
    private static partial void LogFetchGeofenceDataFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to fetch map URL for area {AreaName} from Poracle API")]
    private static partial void LogFetchAreaMapFailed(ILogger logger, Exception ex, string areaName);
}
