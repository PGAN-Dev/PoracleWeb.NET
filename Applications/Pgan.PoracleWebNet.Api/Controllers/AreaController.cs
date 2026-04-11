using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Pgan.PoracleWebNet.Core.Abstractions.Services;

namespace Pgan.PoracleWebNet.Api.Controllers;

[Route("api/areas")]
public partial class AreaController(
    IPoracleHumanProxy humanProxy,
    IPoracleApiProxy poracleApiProxy,
    IUserGeofenceService userGeofenceService,
    ILogger<AreaController> logger) : BaseApiController
{
    private readonly IPoracleHumanProxy _humanProxy = humanProxy;
    private readonly IPoracleApiProxy _poracleApiProxy = poracleApiProxy;
    private readonly IUserGeofenceService _userGeofenceService = userGeofenceService;
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

        // PoracleNG handles the dual-write to humans.area + profiles.area atomically.
        // It also silently filters out any area name whose fence has userSelectable=false,
        // which includes every user-drawn custom geofence (PoracleWeb's feed serves them
        // as userSelectable=false to hide them from the Poracle bot's area picker).
        await this._humanProxy.SetAreasAsync(this.UserId, normalizedAreas);

        // HACK: trusted-set-areas (see docs/poracleng-enhancement-requests.md)
        // Re-add any user-owned custom geofences that were in the submitted list by writing
        // directly to humans.area + active profiles.area — bypassing the setAreas filter.
        // Without this merge, saving on the Areas page would strip every user geofence the
        // user has activated via the Geofences page. Remove this call once PoracleNG ships
        // a trusted setAreas variant that skips the userSelectable intersection.
        //
        // The returned list is always a subset of `normalizedAreas` (it's filtered down to the
        // user-owned subset), so the effective response is just `normalizedAreas` — no Union
        // needed. The discard is intentional.
        _ = await this._userGeofenceService.PreserveOwnedAreasInHumanAsync(this.UserId, normalizedAreas);

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
