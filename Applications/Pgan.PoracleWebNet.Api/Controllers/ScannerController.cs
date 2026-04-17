using System.Data.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Api.Controllers;

[Route("api/scanner")]
public class ScannerController(
    ILogger<ScannerController> logger,
    IScannerService? scannerService = null,
    IKojiService? kojiService = null) : BaseApiController
{
    private const int MinSearchLength = 2;
    private const int MaxSearchLength = 100;
    private const int MaxLimit = 50;
    private const int MaxIdLength = 128;

    private readonly ILogger<ScannerController> _logger = logger;
    private readonly IScannerService? _scannerService = scannerService;
    private readonly IKojiService? _kojiService = kojiService;

    [HttpGet("quests")]
    public async Task<IActionResult> GetActiveQuests()
    {
        if (this._scannerService == null)
        {
            return this.NotFound(new
            {
                message = "Scanner database not configured."
            });
        }

        try
        {
            var quests = await this._scannerService.GetActiveQuestsAsync();
            return this.Ok(quests);
        }
        catch (Exception ex) when (ex is DbException or InvalidOperationException)
        {
            this._logger.LogError(ex, "Scanner DB query failed for GetActiveQuests");
            return this.Ok(Array.Empty<object>());
        }
    }

    [HttpGet("raids")]
    public async Task<IActionResult> GetActiveRaids()
    {
        if (this._scannerService == null)
        {
            return this.NotFound(new
            {
                message = "Scanner database not configured."
            });
        }

        try
        {
            var raids = await this._scannerService.GetActiveRaidsAsync();
            return this.Ok(raids);
        }
        catch (Exception ex) when (ex is DbException or InvalidOperationException)
        {
            this._logger.LogError(ex, "Scanner DB query failed for GetActiveRaids");
            return this.Ok(Array.Empty<object>());
        }
    }

    [HttpGet("max-battle-pokemon")]
    public async Task<IActionResult> GetMaxBattlePokemon()
    {
        if (this._scannerService == null)
        {
            return this.Ok(Array.Empty<int>());
        }

        try
        {
            var pokemonIds = await this._scannerService.GetMaxBattlePokemonIdsAsync();
            return this.Ok(pokemonIds);
        }
        catch (Exception ex) when (ex is DbException or InvalidOperationException)
        {
            this._logger.LogError(ex, "Scanner DB query failed for GetMaxBattlePokemon");
            return this.Ok(Array.Empty<int>());
        }
    }

    [HttpGet("gyms/{id}")]
    [EnableRateLimiting("scanner-search")]
    public async Task<IActionResult> GetGymById(string id)
    {
        if (this._scannerService == null || string.IsNullOrWhiteSpace(id) || id.Length > MaxIdLength)
        {
            return this.NotFound();
        }

        try
        {
            var gym = await this._scannerService.GetGymByIdAsync(id);
            if (gym == null)
            {
                return this.NotFound();
            }

            await this.ResolveGymAreaAsync(gym);
            return this.Ok(gym);
        }
        catch (Exception ex) when (ex is DbException or InvalidOperationException)
        {
            this._logger.LogError(ex, "Scanner DB query failed for GetGymById {GymId}", id);
            return this.NotFound();
        }
    }

    [HttpGet("gyms")]
    [EnableRateLimiting("scanner-search")]
    public async Task<IActionResult> SearchGyms([FromQuery] string search = "", [FromQuery] int limit = 20)
    {
        var trimmed = (search ?? string.Empty).Trim();
        if (this._scannerService == null || trimmed.Length < MinSearchLength || trimmed.Length > MaxSearchLength)
        {
            return this.Ok(Array.Empty<object>());
        }

        var safeLimit = Math.Clamp(limit, 1, MaxLimit);

        try
        {
            var gyms = (await this._scannerService.SearchGymsAsync(trimmed, safeLimit)).ToList();

            if (gyms.Count > 0)
            {
                await this.ResolveGymAreasAsync(gyms);
            }

            return this.Ok(gyms);
        }
        catch (Exception ex) when (ex is DbException or InvalidOperationException)
        {
            this._logger.LogError(ex, "Scanner DB query failed for SearchGyms");
            return this.Ok(Array.Empty<object>());
        }
    }

    private async Task ResolveGymAreaAsync(Core.Models.GymSearchResult gym)
    {
        if (this._kojiService == null)
        {
            return;
        }

        var fences = await this._kojiService.GetAdminGeofencesAsync();
        foreach (var fence in fences)
        {
            if (FenceContains(fence, gym.Lat, gym.Lon))
            {
                gym.Area = fence.Name;
                return;
            }
        }
    }

    private async Task ResolveGymAreasAsync(IReadOnlyList<Core.Models.GymSearchResult> gyms)
    {
        if (this._kojiService == null)
        {
            return;
        }

        var fences = await this._kojiService.GetAdminGeofencesAsync();
        foreach (var gym in gyms)
        {
            foreach (var fence in fences)
            {
                if (FenceContains(fence, gym.Lat, gym.Lon))
                {
                    gym.Area = fence.Name;
                    break;
                }
            }
        }
    }

    private static bool FenceContains(Core.Models.AdminGeofence fence, double lat, double lon)
    {
        if (lat < fence.MinLat || lat > fence.MaxLat || lon < fence.MinLon || lon > fence.MaxLon)
        {
            return false;
        }

        return GeometryHelpers.PointInPolygon(lat, lon, fence.Path);
    }
}
