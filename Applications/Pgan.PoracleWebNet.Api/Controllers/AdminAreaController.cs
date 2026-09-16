using Microsoft.AspNetCore.Mvc;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Api.Controllers;

/// <summary>
/// Lets an admin take an area off the menu: staging fences, test polygons, regions the instance
/// covers but does not advertise.
/// </summary>
/// <remarks>
/// <para>
/// Separate from <c>GET /api/areas/available</c> on purpose. That endpoint drops anything marked
/// <c>userSelectable: false</c> for every caller, admins included (#544), so an admin reading it could
/// see what is still on offer but never what they had hidden — the list would be one-way and nothing
/// could be un-hidden. This reads Koji directly and reports the hidden state alongside.
/// </para>
/// <para>
/// Writes land in the <c>hidden_areas</c> site setting and take effect through
/// <c>GeofenceFeedController</c>, which serves those fences with <c>userSelectable: false</c>.
/// </para>
/// </remarks>
[Route("api/admin/areas")]
public partial class AdminAreaController(
    IKojiService kojiService,
    ISiteSettingService siteSettingService,
    IPoracleApiProxy poracleApiProxy,
    ILogger<AdminAreaController> logger) : BaseApiController
{
    private readonly IKojiService _kojiService = kojiService;
    private readonly ISiteSettingService _siteSettingService = siteSettingService;
    private readonly IPoracleApiProxy _poracleApiProxy = poracleApiProxy;
    private readonly ILogger<AdminAreaController> _logger = logger;

    /// <summary>Every admin area Koji serves, each flagged with whether it is currently hidden.</summary>
    [HttpGet]
    public async Task<IActionResult> GetAreas()
    {
        if (!this.IsAdmin)
        {
            return this.Forbid();
        }

        var hidden = HiddenAreas.Parse((await this._siteSettingService.GetByKeyAsync(HiddenAreas.SettingKey))?.Value);

        IEnumerable<AdminGeofence> areas;
        try
        {
            areas = await this._kojiService.GetAdminGeofencesAsync();
        }
        catch (Exception ex)
        {
            LogFetchAreasFailed(this._logger, ex);
            return this.StatusCode(503, new
            {
                error = "Could not read areas from Koji."
            });
        }

        var rows = areas
            .Select(a => new
            {
                name = a.Name,
                group = a.Group,
                // Hidden by this site's own list, which the page can toggle.
                hidden = hidden.Contains(a.Name),
                // Already non-selectable in Koji itself. Shown, not editable: clearing our flag would
                // not make it selectable, and offering a toggle that cannot deliver is worse than
                // saying why.
                hiddenInKoji = !a.UserSelectable,
            })
            .OrderBy(a => a.group, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Names we hide that Koji no longer serves. Left in the setting rather than pruned silently,
        // because a Koji outage must not look like a deletion, but surfaced so an operator can tell a
        // stale entry from a working one.
        var live = areas.Select(a => HiddenAreas.Normalize(a.Name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var orphaned = hidden.Where(h => !live.Contains(h)).OrderBy(h => h, StringComparer.Ordinal).ToList();

        return this.Ok(new
        {
            areas = rows,
            orphaned,
        });
    }

    /// <summary>Replaces the hidden list wholesale.</summary>
    /// <remarks>
    /// Takes the whole list rather than a per-area toggle so two admins editing at once cannot
    /// interleave into a state neither chose, and so clearing the list is expressible.
    /// </remarks>
    [HttpPut]
    public async Task<IActionResult> SetHidden([FromBody] SetHiddenAreasRequest request)
    {
        if (!this.IsAdmin)
        {
            return this.Forbid();
        }

        var serialized = HiddenAreas.Serialize(request.Names ?? []);
        if (!HiddenAreas.TryValidate(serialized, out var error))
        {
            return this.BadRequest(new
            {
                error
            });
        }

        await this._siteSettingService.CreateOrUpdateAsync(new SiteSetting
        {
            Key = HiddenAreas.SettingKey,
            Value = serialized,
            Category = "areas",
            ValueType = "json",
        });

        // Poracle holds the feed in memory and re-reads it on its own schedule, so without this the
        // change would not reach the bot's area picker until the next periodic reload. The site's own
        // Areas page reads through Poracle too, so this is what makes the toggle take effect anywhere.
        try
        {
            await this._poracleApiProxy.ReloadGeofencesAsync();
        }
        catch (Exception ex)
        {
            // The setting is saved either way. Report it rather than failing the write, so an operator
            // is told "saved, not yet live" instead of retrying a save that already succeeded.
            LogReloadFailed(this._logger, ex);
            return this.Ok(new
            {
                saved = true,
                reloaded = false,
            });
        }

        return this.Ok(new
        {
            saved = true,
            reloaded = true,
        });
    }

    public class SetHiddenAreasRequest
    {
        public string[]? Names
        {
            get; set;
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to read admin areas from Koji")]
    private static partial void LogFetchAreasFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Hidden areas saved, but the Poracle geofence reload failed — the change lands on Poracle's next periodic reload")]
    private static partial void LogReloadFailed(ILogger logger, Exception ex);
}
