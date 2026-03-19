using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PGAN.Poracle.Web.Core.Abstractions.Services;
using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Api.Controllers;

[Route("api/config")]
public class ConfigController(IPoracleApiProxy poracleApiProxy, ILogger<ConfigController> logger) : BaseApiController
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
            this._logger.LogWarning(ex, "Failed to fetch Poracle templates");
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
            this._logger.LogWarning(ex, "Failed to fetch Poracle config");
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
}
