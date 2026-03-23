using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Api.Controllers;

[Route("api/settings")]
public class SettingsController(IPwebSettingService settingService) : BaseApiController
{
    private static readonly HashSet<string> PublicKeys = new(StringComparer.OrdinalIgnoreCase) { "custom_title" };

    private readonly IPwebSettingService _settingService = settingService;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var settings = await this._settingService.GetAllAsync();
        return this.Ok(settings);
    }

    [AllowAnonymous]
    [HttpGet("public")]
    public async Task<IActionResult> GetPublic()
    {
        var settings = await this._settingService.GetAllAsync();
        var publicSettings = settings.Where(s => s.Setting != null && PublicKeys.Contains(s.Setting)).ToList();
        return this.Ok(publicSettings);
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
