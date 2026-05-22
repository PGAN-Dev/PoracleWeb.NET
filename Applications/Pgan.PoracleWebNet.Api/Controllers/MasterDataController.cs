using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pgan.PoracleWebNet.Core.Abstractions.Services;

namespace Pgan.PoracleWebNet.Api.Controllers;

[Route("api/masterdata")]
public class MasterDataController(
    IMasterDataService masterDataService,
    IPoracleApiProxy poracleApiProxy,
    IRaidLevelService raidLevelService) : BaseApiController
{
    private readonly IMasterDataService _masterDataService = masterDataService;
    private readonly IPoracleApiProxy _poracleApiProxy = poracleApiProxy;
    private readonly IRaidLevelService _raidLevelService = raidLevelService;

    [AllowAnonymous]
    [HttpGet("pokemon")]
    public async Task<IActionResult> GetPokemon()
    {
        var data = await this._masterDataService.GetPokemonDataAsync();
        if (data == null)
        {
            await this._masterDataService.RefreshCacheAsync();
            data = await this._masterDataService.GetPokemonDataAsync();
        }

        if (data == null)
        {
            return this.NotFound(new
            {
                message = "Pokemon data not available."
            });
        }

        return this.Content(data, "application/json");
    }

    [AllowAnonymous]
    [HttpGet("items")]
    public async Task<IActionResult> GetItems()
    {
        var data = await this._masterDataService.GetItemDataAsync();
        if (data == null)
        {
            await this._masterDataService.RefreshCacheAsync();
            data = await this._masterDataService.GetItemDataAsync();
        }

        if (data == null)
        {
            return this.NotFound(new
            {
                message = "Item data not available."
            });
        }

        return this.Content(data, "application/json");
    }

    /// <summary>
    /// Canonical raid-level vocabulary (currently 19 levels from the WatWowMap masterfile).
    /// Cached server-side; the frontend uses this to render the level selector and
    /// fall back to bare integers for any level not in the list.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("raid-levels")]
    public async Task<IActionResult> GetRaidLevels()
    {
        var levels = await this._raidLevelService.GetAllAsync();
        return this.Ok(levels);
    }

    [AllowAnonymous]
    [HttpGet("grunts")]
    public async Task<IActionResult> GetGrunts()
    {
        var grunts = await this._poracleApiProxy.GetGruntsAsync();
        if (grunts == null)
        {
            return this.NotFound(new
            {
                message = "Grunt data not available."
            });
        }

        return this.Content(grunts, "application/json");
    }
}
