using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Api.Controllers;

[Route("api/settings")]
public class SettingsController(ISiteSettingService siteSettingService) : BaseApiController
{
    private static readonly HashSet<string> SensitiveKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "api_secret", "telegram_bot_token", "scan_db",
    };

    private readonly ISiteSettingService _siteSettingService = siteSettingService;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var settings = await this._siteSettingService.GetAllAsync();

        // Non-admin users only see non-sensitive settings
        if (!this.IsAdmin)
        {
            settings = settings.Where(s => !SensitiveKeys.Contains(s.Key));
        }

        return this.Ok(settings);
    }

    [AllowAnonymous]
    [EnableRateLimiting("auth-read")]
    [HttpGet("public")]
    public async Task<IActionResult> GetPublic()
    {
        var publicSettings = await this._siteSettingService.GetPublicAsync();
        return this.Ok(publicSettings);
    }

    [HttpPut("{key}")]
    public async Task<IActionResult> Upsert(string key, [FromBody] SiteSettingRequest request)
    {
        if (!this.IsAdmin)
        {
            return this.Forbid();
        }

        // Preserve existing category and valueType if not provided in the request
        var existing = await this._siteSettingService.GetByKeyAsync(key);

        var setting = new SiteSetting
        {
            Key = key,
            Value = request.Value,
            Category = request.Category ?? existing?.Category ?? string.Empty,
            ValueType = request.ValueType ?? existing?.ValueType ?? "string",
        };

        var result = await this._siteSettingService.CreateOrUpdateAsync(setting);
        return this.Ok(result);
    }

    public class SiteSettingRequest
    {
        public string? Value { get; set; }
        public string? Category { get; set; }
        public string? ValueType { get; set; }
    }
}
