using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Api.Controllers;

[Route("api/config")]
public partial class ConfigController(IPoracleApiProxy poracleApiProxy, ILogger<ConfigController> logger) : BaseApiController
{
    private readonly IPoracleApiProxy _poracleApiProxy = poracleApiProxy;
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

    [AllowAnonymous]
    [HttpGet]
    public async Task<IActionResult> GetConfig()
    {
        try
        {
            var config = await this._poracleApiProxy.GetConfigAsync();
            if (config != null)
            {
                return this.Ok(config);
            }
        }
        catch (Exception ex)
        {
            LogFetchConfigFailed(this._logger, ex);
        }

        return this.Ok(new PoracleConfig
        {
            Locale = "en",
            ProviderUrl = "",
            StaticKey = "",
            PoracleVersion = "unknown",
            PvpFilterMaxRank = 100,
            PvpFilterLittleMinCp = 0,
            PvpFilterGreatMinCp = 0,
            PvpFilterUltraMinCp = 0,
            PvpLittleLeagueAllowed = true,
            DefaultTemplateName = "default",
            EverythingFlagPermissions = "",
            MaxDistance = 10726000
        });
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to fetch Poracle templates")]
    private static partial void LogFetchTemplatesFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to fetch Poracle config")]
    private static partial void LogFetchConfigFailed(ILogger logger, Exception ex);
}
