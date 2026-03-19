using Microsoft.AspNetCore.Mvc;
using PGAN.Poracle.Web.Core.Abstractions.Services;
using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Api.Controllers;

[Route("api/settings")]
public class SettingsController(IPwebSettingService settingService) : BaseApiController
{
    private readonly IPwebSettingService _settingService = settingService;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var settings = await this._settingService.GetAllAsync();
        return this.Ok(settings);
    }

    [HttpPut("{key}")]
    public async Task<IActionResult> Upsert(string key, [FromBody] PwebSetting setting)
    {
        if (!this.IsAdmin)
        {
            return this.Forbid();
        }

        setting.Setting = key;
        var result = await this._settingService.CreateOrUpdateAsync(setting);
        return this.Ok(result);
    }
}
