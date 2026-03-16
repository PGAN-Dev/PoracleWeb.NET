using Microsoft.AspNetCore.Mvc;
using PGAN.Poracle.Web.Core.Abstractions.Services;
using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Api.Controllers;

[Route("api/settings")]
public class SettingsController : BaseApiController
{
    private readonly IPwebSettingService _settingService;

    public SettingsController(IPwebSettingService settingService)
    {
        _settingService = settingService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var settings = await _settingService.GetAllAsync();
        return Ok(settings);
    }

    [HttpPut("{key}")]
    public async Task<IActionResult> Upsert(string key, [FromBody] PwebSetting setting)
    {
        if (!IsAdmin)
            return Forbid();

        setting.Setting = key;
        var result = await _settingService.CreateOrUpdateAsync(setting);
        return Ok(result);
    }
}
