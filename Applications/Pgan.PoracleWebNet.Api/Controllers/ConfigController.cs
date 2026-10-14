using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Api.Controllers;

[Route("api/config")]
public partial class ConfigController(
    IPoracleApiProxy poracleApiProxy,
    IPoracleV2SchemaService v2Schema,
    ILogger<ConfigController> logger) : BaseApiController
{
    private readonly IPoracleApiProxy _poracleApiProxy = poracleApiProxy;
    private readonly IPoracleV2SchemaService _v2Schema = v2Schema;
    private readonly ILogger<ConfigController> _logger = logger;

    [AllowAnonymous]
    [HttpGet("templates")]
    public async Task<IActionResult> GetTemplates()
    {
        try
        {
            var templates = await this._poracleApiProxy.GetTemplatesAsync();
            if (templates != null)
            {
                return this.Content(templates, "application/json");
            }
        }
        catch (Exception ex)
        {
            LogFetchTemplatesFailed(this._logger, ex);
        }

        return this.Ok(new
        {
            status = "ok",
            discord = new
            {
            },
            telegram = new
            {
            }
        });
    }

    [AllowAnonymous]
    [HttpGet("dts")]
    public IActionResult GetDts()
    {
        var json = Services.DtsCacheService.GetCachedDts();
        if (!string.IsNullOrEmpty(json))
        {
            return this.Content(json, "application/json");
        }

        return this.Ok(Array.Empty<object>());
    }

    /// <summary>
    /// Poracle's client-facing configuration. Authenticated: the only consumers are the Pokemon
    /// add/edit dialogs, which sit behind the auth guard, and the payload is projected through
    /// <see cref="PublicPoracleConfig"/> so the admin id lists, the delegation map, the provider URL
    /// and the static key never reach a browser at all.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetConfig()
    {
        // Degrades to false on any fault, same as every other v2 schema capability -- an operator cannot
        // preserve a mention through an edit until this answers true, so treating "could not ask" as "no"
        // is the only safe direction here.
        var pingSupported = (await this._v2Schema.GetAsync()).TrackingPing;

        try
        {
            var config = await this._poracleApiProxy.GetConfigAsync();
            if (config != null)
            {
                var publicConfig = PublicPoracleConfig.From(config);
                publicConfig.PingSupported = pingSupported;
                return this.Ok(publicConfig);
            }
        }
        catch (Exception ex)
        {
            LogFetchConfigFailed(this._logger, ex);
        }

        return this.Ok(new PublicPoracleConfig
        {
            Locale = "en",
            PoracleVersion = "unknown",
            PvpFilterMaxRank = 100,
            PvpFilterLittleMinCp = 0,
            PvpFilterGreatMinCp = 0,
            PvpFilterUltraMinCp = 0,
            PvpLittleLeagueAllowed = true,
            DefaultTemplateName = "default",
            EverythingFlagPermissions = "",
            MaxDistance = 10726000,
            PingSupported = pingSupported,
        });
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to fetch Poracle templates")]
    private static partial void LogFetchTemplatesFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to fetch Poracle config")]
    private static partial void LogFetchConfigFailed(ILogger logger, Exception ex);
}
