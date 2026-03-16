using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using PGAN.Poracle.Web.Core.Abstractions.Services;

namespace PGAN.Poracle.Web.Api.Controllers;

[Route("api/areas")]
public class AreaController : BaseApiController
{
    private readonly IHumanService _humanService;
    private readonly IPoracleApiProxy _poracleApiProxy;
    private readonly ILogger<AreaController> _logger;

    public AreaController(IHumanService humanService, IPoracleApiProxy poracleApiProxy, ILogger<AreaController> logger)
    {
        _humanService = humanService;
        _poracleApiProxy = poracleApiProxy;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetSelectedAreas()
    {
        var human = await _humanService.GetByIdAsync(UserId);
        if (human == null)
            return NotFound();

        // Area is stored as a JSON array in the DB, e.g. ["west end", "downtown"]
        var areas = Array.Empty<string>();
        if (!string.IsNullOrWhiteSpace(human.Area))
        {
            try
            {
                areas = JsonSerializer.Deserialize<string[]>(human.Area) ?? [];
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse area JSON for user {UserId}, falling back to comma-separated", UserId);
                // Fallback: treat as comma-separated
                areas = human.Area.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            }
        }

        return Ok(areas);
    }

    [HttpGet("available")]
    public async Task<IActionResult> GetAvailableAreas()
    {
        try
        {
            var areasJson = await _poracleApiProxy.GetAreasWithGroupsAsync(UserId);
            if (areasJson != null)
                return Content(areasJson, "application/json");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch available areas from Poracle API for user {UserId}", UserId);
        }

        return Ok(Array.Empty<object>());
    }

    [HttpPut]
    public async Task<IActionResult> UpdateAreas([FromBody] UpdateAreasRequest request)
    {
        var human = await _humanService.GetByIdAsync(UserId);
        if (human == null)
            return NotFound();

        // Store as JSON array to match Poracle's format
        human.Area = request.Areas != null && request.Areas.Length > 0
            ? JsonSerializer.Serialize(request.Areas)
            : "[]";

        await _humanService.UpdateAsync(human);
        return Ok(request.Areas ?? []);
    }

    [HttpGet("geofence")]
    public async Task<IActionResult> GetGeofencePolygons()
    {
        try
        {
            var json = await _poracleApiProxy.GetAllGeofenceDataAsync();
            if (json != null)
                return Content(json, "application/json");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch geofence data from Poracle API");
        }

        return Ok(new { status = "ok", geofence = Array.Empty<object>() });
    }

    [HttpGet("map/{areaName}")]
    public async Task<IActionResult> GetAreaMap(string areaName)
    {
        try
        {
            var mapUrl = await _poracleApiProxy.GetAreaMapUrlAsync(Uri.UnescapeDataString(areaName));
            if (mapUrl != null)
                return Ok(new { url = mapUrl });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch map URL for area {AreaName} from Poracle API", areaName);
        }

        return NotFound();
    }

    public class UpdateAreasRequest
    {
        public string[]? Areas { get; set; }
    }
}
