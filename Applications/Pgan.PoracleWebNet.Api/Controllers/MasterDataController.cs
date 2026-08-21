using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pgan.PoracleWebNet.Core.Abstractions.Services;

namespace Pgan.PoracleWebNet.Api.Controllers;

[Route("api/masterdata")]
public partial class MasterDataController(
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
    /// <summary>
    /// Move ID to name map. Used to label the charged moves on Max Battle alarms, which otherwise
    /// render as bare <c>Move #123</c>.
    /// </summary>
    [HttpGet("moves")]
    public async Task<IActionResult> GetMoves()
    {
        var data = await this._masterDataService.GetMoveDataAsync();
        if (data == null)
        {
            await this._masterDataService.RefreshCacheAsync();
            data = await this._masterDataService.GetMoveDataAsync();
        }

        if (data == null)
        {
            return this.NotFound(new
            {
                message = "Move data not available."
            });
        }

        return this.Content(data, "application/json");
    }

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

    /// <summary>
    /// Monster master data (names, types, form names, stats, evolutions) keyed
    /// <c>"{pokemonId}_{formId}"</c>, translated into <paramref name="locale"/>.
    /// </summary>
    /// <remarks>
    /// PoracleNG owns the translations, so this proxies its <c>/api/masterdata/monsters</c> and only
    /// falls back to the WatWowMap masterfile - which is English-only - when that route is missing or
    /// unreachable. Before this endpoint existed the SPA fetched the masterfile from GitHub directly,
    /// so Pokemon names and types stayed English no matter what the display language was set to.
    /// </remarks>
    [AllowAnonymous]
    [HttpGet("monsters")]
    public async Task<IActionResult> GetMonsters([FromQuery] string? locale)
    {
        var requested = NormalizeLocale(locale);

        try
        {
            var localized = await this._poracleApiProxy.GetMonstersAsync(requested);
            if (!string.IsNullOrWhiteSpace(localized))
            {
                return this.Content(localized, "application/json");
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Upstream unreachable or too slow - fall through to the English masterfile rather than
            // leaving the selector with no names, types or forms at all. A misconfigured
            // Poracle:ApiAddress throws InvalidOperationException instead and is left to surface.
        }

        var fallback = await this._masterDataService.GetMonsterDataAsync();
        if (fallback == null)
        {
            await this._masterDataService.RefreshCacheAsync();
            fallback = await this._masterDataService.GetMonsterDataAsync();
        }

        if (fallback == null)
        {
            return this.NotFound(new
            {
                message = "Monster data not available."
            });
        }

        return this.Content(fallback, "application/json");
    }

    /// <summary>
    /// Constrains the locale to a BCP-47-ish shape before it reaches the upstream query string.
    /// Anything else becomes <c>en</c>, which is also what PoracleNG defaults to.
    /// </summary>
    internal static string NormalizeLocale(string? locale)
    {
        if (string.IsNullOrWhiteSpace(locale))
        {
            return "en";
        }

        var trimmed = locale.Trim();
        return LocalePattern().IsMatch(trimmed) ? trimmed : "en";
    }

    [System.Text.RegularExpressions.GeneratedRegex("^[a-zA-Z]{2,3}(-[a-zA-Z0-9]{2,8})?$")]
    private static partial System.Text.RegularExpressions.Regex LocalePattern();

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
