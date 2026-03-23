using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Pgan.PoracleWebNet.Core.Abstractions.Services;

namespace Pgan.PoracleWebNet.Api.Controllers;

[Route("api/areas")]
public partial class AreaController(IHumanService humanService, IProfileService profileService, IPoracleApiProxy poracleApiProxy, ILogger<AreaController> logger) : BaseApiController
{
    private readonly IHumanService _humanService = humanService;
    private readonly IProfileService _profileService = profileService;
    private readonly IPoracleApiProxy _poracleApiProxy = poracleApiProxy;
    private readonly ILogger<AreaController> _logger = logger;

    [HttpGet]
    public async Task<IActionResult> GetSelectedAreas()
    {
        // Read from profiles.area for the current profile (profile-scoped)
        var profile = await this._profileService.GetByUserAndProfileNoAsync(this.UserId, this.ProfileNo);
        if (profile == null)
        {
            // Fallback to humans.area if profile not found
            var human = await this._humanService.GetByIdAsync(this.UserId);
            if (human == null)
            {
                return this.NotFound();
            }

            return this.Ok(ParseAreaJson(human.Area));
        }

        return this.Ok(ParseAreaJson(profile.Area));
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
        var human = await this._humanService.GetByIdAsync(this.UserId);
        if (human == null)
        {
            return this.NotFound();
        }

        // Lowercase area names to match Poracle's expected format (PHP PoracleWeb does strtolower)
        var normalizedAreas = request.Areas != null && request.Areas.Length > 0
            ? request.Areas.Select(a => a.ToLowerInvariant()).ToArray()
            : [];

        var areaJson = normalizedAreas.Length > 0
            ? JsonSerializer.Serialize(normalizedAreas)
            : "[]";

        // Update humans.area (PoracleJS reads this for notifications)
        human.Area = areaJson;
        await this._humanService.UpdateAsync(human);

        // Also update profiles.area for the current profile (profile-scoped storage)
        var profile = await this._profileService.GetByUserAndProfileNoAsync(this.UserId, this.ProfileNo);
        if (profile != null)
        {
            profile.Area = areaJson;
            await this._profileService.UpdateAsync(profile);
        }

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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to parse area JSON for user {UserId}, falling back to comma-separated")]
    private static partial void LogParseAreaJsonFailed(ILogger logger, Exception ex, string userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to fetch available areas from Poracle API for user {UserId}")]
    private static partial void LogFetchAvailableAreasFailed(ILogger logger, Exception ex, string userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to fetch geofence data from Poracle API")]
    private static partial void LogFetchGeofenceDataFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to fetch map URL for area {AreaName} from Poracle API")]
    private static partial void LogFetchAreaMapFailed(ILogger logger, Exception ex, string areaName);
}
