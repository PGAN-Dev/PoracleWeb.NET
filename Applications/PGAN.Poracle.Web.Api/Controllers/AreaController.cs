using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using PGAN.Poracle.Web.Core.Abstractions.Services;

namespace PGAN.Poracle.Web.Api.Controllers;

[Route("api/areas")]
public class AreaController(IHumanService humanService, IPoracleApiProxy poracleApiProxy, ILogger<AreaController> logger) : BaseApiController
{
    private readonly IHumanService _humanService = humanService;
    private readonly IPoracleApiProxy _poracleApiProxy = poracleApiProxy;
    private readonly ILogger<AreaController> _logger = logger;

    [HttpGet]
    public async Task<IActionResult> GetSelectedAreas()
    {
        var human = await this._humanService.GetByIdAsync(this.UserId);
        if (human == null)
        {
            return this.NotFound();
        }

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
                this._logger.LogWarning(ex, "Failed to parse area JSON for user {UserId}, falling back to comma-separated", this.UserId);
                // Fallback: treat as comma-separated
                areas = human.Area.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            }
        }

        return this.Ok(areas);
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
            this._logger.LogWarning(ex, "Failed to fetch available areas from Poracle API for user {UserId}", this.UserId);
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

        // Store as JSON array to match Poracle's format
        human.Area = request.Areas != null && request.Areas.Length > 0
            ? JsonSerializer.Serialize(request.Areas)
            : "[]";

        await this._humanService.UpdateAsync(human);
        return this.Ok(request.Areas ?? []);
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
            this._logger.LogWarning(ex, "Failed to fetch geofence data from Poracle API");
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
            this._logger.LogWarning(ex, "Failed to fetch map URL for area {AreaName} from Poracle API", areaName);
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
}
