using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PGAN.Poracle.Web.Core.Abstractions.Services;
using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Api.Controllers;

[Route("api/config")]
public class ConfigController : BaseApiController
{
    private readonly IPoracleApiProxy _poracleApiProxy;
    private readonly ILogger<ConfigController> _logger;

    public ConfigController(IPoracleApiProxy poracleApiProxy, ILogger<ConfigController> logger)
    {
        _poracleApiProxy = poracleApiProxy;
        _logger = logger;
    }

    [AllowAnonymous]
    [HttpGet("templates")]
    public async Task<IActionResult> GetTemplates()
    {
        try
        {
            var templates = await _poracleApiProxy.GetTemplatesAsync();
            if (templates != null)
                return Content(templates, "application/json");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch Poracle templates");
        }

        return Ok(new { status = "ok", discord = new { }, telegram = new { } });
    }

    [AllowAnonymous]
    [HttpGet("dts")]
    public IActionResult GetDts()
    {
        var json = Services.DtsCacheService.GetCachedDts();
        if (!string.IsNullOrEmpty(json))
            return Content(json, "application/json");

        return Ok(Array.Empty<object>());
    }

    [AllowAnonymous]
    [HttpGet]
    public async Task<IActionResult> GetConfig()
    {
        try
        {
            var config = await _poracleApiProxy.GetConfigAsync();
            if (config != null)
                return Ok(config);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch Poracle config");
        }

        return Ok(new PoracleConfig
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
