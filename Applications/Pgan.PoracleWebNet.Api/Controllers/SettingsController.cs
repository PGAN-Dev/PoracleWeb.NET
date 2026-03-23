using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Api.Controllers;

[Route("api/settings")]
public class SettingsController(ISiteSettingService siteSettingService) : BaseApiController
{
    private readonly ISiteSettingService _siteSettingService = siteSettingService;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var settings = await this._siteSettingService.GetAllAsync();
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

        var setting = new SiteSetting
        {
            // Support both "key" and legacy "setting" property name from the request body
            Key = key,
            Value = request.Value,
            Category = request.Category
        };

        var result = await this._siteSettingService.CreateOrUpdateAsync(setting);
        return this.Ok(result);
    }

    /// <summary>
    /// Request body for PUT /api/settings/{key}.
    /// Accepts both new shape (key/value/category) and legacy shape (setting/value).
    /// </summary>
    public class SiteSettingRequest
    {
        /// <summary>New property name.</summary>
        public string? Key
        {
            get; set;
        }

        /// <summary>Legacy property name — mapped to Key for backward compatibility.</summary>
        public string? Setting
        {
            get; set;
        }

        public string? Value
        {
            get; set;
        }

        public string? Category
        {
            get; set;
        }
    }
}
