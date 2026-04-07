using Microsoft.AspNetCore.Mvc;
using Pgan.PoracleWebNet.Core.Abstractions.Services;

namespace Pgan.PoracleWebNet.Api.Controllers;

[Route("api/pokemon-availability")]
public class PokemonAvailabilityController(IPokemonAvailabilityService? availabilityService = null) : BaseApiController
{
    private readonly IPokemonAvailabilityService? _availabilityService = availabilityService;

    [HttpGet]
    public async Task<IActionResult> GetAvailablePokemon()
    {
        if (this._availabilityService == null)
        {
            return this.Ok(new
            {
                available = Array.Empty<int>(),
                enabled = false
            });
        }

        var ids = await this._availabilityService.GetAvailablePokemonIdsAsync();
        return this.Ok(new
        {
            available = ids,
            enabled = true
        });
    }
}
