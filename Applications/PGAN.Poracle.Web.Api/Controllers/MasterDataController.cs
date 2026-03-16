using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PGAN.Poracle.Web.Core.Abstractions.Services;

namespace PGAN.Poracle.Web.Api.Controllers;

[Route("api/masterdata")]
public class MasterDataController : BaseApiController
{
    private readonly IMasterDataService _masterDataService;
    private readonly IPoracleApiProxy _poracleApiProxy;

    public MasterDataController(IMasterDataService masterDataService, IPoracleApiProxy poracleApiProxy)
    {
        _masterDataService = masterDataService;
        _poracleApiProxy = poracleApiProxy;
    }

    [AllowAnonymous]
    [HttpGet("pokemon")]
    public async Task<IActionResult> GetPokemon()
    {
        var data = await _masterDataService.GetPokemonDataAsync();
        if (data == null)
        {
            await _masterDataService.RefreshCacheAsync();
            data = await _masterDataService.GetPokemonDataAsync();
        }

        if (data == null)
            return NotFound(new { message = "Pokemon data not available." });

        return Content(data, "application/json");
    }

    [AllowAnonymous]
    [HttpGet("items")]
    public async Task<IActionResult> GetItems()
    {
        var data = await _masterDataService.GetItemDataAsync();
        if (data == null)
        {
            await _masterDataService.RefreshCacheAsync();
            data = await _masterDataService.GetItemDataAsync();
        }

        if (data == null)
            return NotFound(new { message = "Item data not available." });

        return Content(data, "application/json");
    }

    [AllowAnonymous]
    [HttpGet("grunts")]
    public async Task<IActionResult> GetGrunts()
    {
        var grunts = await _poracleApiProxy.GetGruntsAsync();
        if (grunts == null)
            return NotFound(new { message = "Grunt data not available." });

        return Content(grunts, "application/json");
    }
}
