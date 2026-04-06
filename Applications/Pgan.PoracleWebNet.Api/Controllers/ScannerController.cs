using Microsoft.AspNetCore.Mvc;
using Pgan.PoracleWebNet.Core.Abstractions.Services;

namespace Pgan.PoracleWebNet.Api.Controllers;

[Route("api/scanner")]
public class ScannerController(IScannerService? scannerService = null, IKojiService? kojiService = null) : BaseApiController
{
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

        var quests = await this._scannerService.GetActiveQuestsAsync();
        return this.Ok(quests);
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

        var raids = await this._scannerService.GetActiveRaidsAsync();
        return this.Ok(raids);
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
        catch
        {
            return this.Ok(Array.Empty<int>());
        }
    }

    [HttpGet("gyms/{id}")]
    public async Task<IActionResult> GetGymById(string id)
    {
        if (this._scannerService == null)
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

            if (this._kojiService != null)
            {
                var fences = await this._kojiService.GetAdminGeofencesAsync();
                foreach (var fence in fences)
                {
                    if (IScannerService.PointInPolygon(gym.Lat, gym.Lon, fence.Path))
                    {
                        gym.Area = fence.Name;
                        break;
                    }
                }
            }

            return this.Ok(gym);
        }
        catch
        {
            return this.NotFound();
        }
    }

    [HttpGet("gyms")]
    public async Task<IActionResult> SearchGyms([FromQuery] string search = "", [FromQuery] int limit = 20)
    {
        if (this._scannerService == null || search.Length < 2)
        {
            return this.Ok(Array.Empty<object>());
        }

        try
        {
            var gyms = (await this._scannerService.SearchGymsAsync(search, Math.Min(limit, 50))).ToList();

            if (this._kojiService != null && gyms.Count > 0)
            {
                var fences = await this._kojiService.GetAdminGeofencesAsync();
                foreach (var gym in gyms)
                {
                    foreach (var fence in fences)
                    {
                        if (IScannerService.PointInPolygon(gym.Lat, gym.Lon, fence.Path))
                        {
                            gym.Area = fence.Name;
                            break;
                        }
                    }
                }
            }

            return this.Ok(gyms);
        }
        catch
        {
            return this.Ok(Array.Empty<object>());
        }
    }
}
