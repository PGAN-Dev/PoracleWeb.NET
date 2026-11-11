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
    /// Costume ID to name map in <paramref name="locale"/>, used to label the costume filter on pokemon and
    /// raid alarms.
    /// </summary>
    /// <remarks>
    /// PoracleNG 5.4.0 serves translated names at <c>/api/masterdata/costumes?locale=</c> (keyed by id, values
    /// <c>{id, name}</c>). This flattens that to the id-to-name map the SPA has always read, dropping id 0
    /// ("Unset", which the dialogs offer as their own "no costume" choice) and any name that is still a bare
    /// <c>costume_N</c> translation key. A server without the route, one that is down, or one whose game data
    /// has not loaded (an empty object) falls back to the English names from the raw WatWowMap masterfile.
    /// </remarks>
    [AllowAnonymous]
    [HttpGet("costumes")]
    public async Task<IActionResult> GetCostumes([FromQuery] string? locale)
    {
        try
        {
            var localized = await this._poracleApiProxy.GetCostumesAsync(NormalizeLocale(locale));
            if (FlattenCostumes(localized) is { Count: > 0 } names)
            {
                return this.Content(System.Text.Json.JsonSerializer.Serialize(names), "application/json");
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            // Upstream unreachable, too slow or answering something that is not its documented shape: the
            // English masterfile names are better than an empty picker.
        }

        var data = await this._masterDataService.GetCostumeDataAsync();
        if (data == null)
        {
            await this._masterDataService.RefreshCacheAsync();
            data = await this._masterDataService.GetCostumeDataAsync();
        }

        if (data == null)
        {
            return this.NotFound(new
            {
                message = "Costume data not available."
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
    /// PoracleNG's costume object (<c>{"1": {"id": 1, "name": "Holiday"}, ...}</c>) as the SPA's id-to-name map.
    /// Null for a null or non-object answer; empty when nothing usable was in it.
    /// </summary>
    private static Dictionary<string, string>? FlattenCostumes(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        using var doc = System.Text.Json.JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
        {
            return null;
        }

        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in doc.RootElement.EnumerateObject())
        {
            if (entry.Name == "0"
                || entry.Value.ValueKind != System.Text.Json.JsonValueKind.Object
                || !entry.Value.TryGetProperty("name", out var name)
                || name.ValueKind != System.Text.Json.JsonValueKind.String)
            {
                continue;
            }

            var text = name.GetString();
            if (string.IsNullOrWhiteSpace(text) || text.StartsWith("costume_", StringComparison.Ordinal))
            {
                continue;
            }

            names[entry.Name] = text;
        }

        return names;
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
    public async Task<IActionResult> GetGrunts([FromQuery] string? locale)
    {
        var grunts = await this._poracleApiProxy.GetGruntsAsync(NormalizeLocale(locale));
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
