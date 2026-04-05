using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Pgan.PoracleWebNet.Api.Configuration;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Api.Controllers;

[Route("api/settings")]
public class SettingsController(
    ISiteSettingService siteSettingService,
    IOptions<DiscordSettings> discordSettings,
    IOptions<PoracleSettings> poracleSettings,
    IOptions<TelegramSettings> telegramSettings) : BaseApiController
{
    private static readonly HashSet<string> SensitiveKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "api_secret", "telegram_bot_token", "scan_db",
        "discord_client_secret", "discord_bot_token",
    };

    private static readonly HashSet<string> InternalKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "migration_completed",
    };

    private const string EnableDiscordKey = "enable_discord";
    private const string EnableTelegramKey = "enable_telegram";

    private readonly DiscordSettings _discordSettings = discordSettings.Value;
    private readonly PoracleSettings _poracleSettings = poracleSettings.Value;
    private readonly TelegramSettings _telegramSettings = telegramSettings.Value;
    private readonly ISiteSettingService _siteSettingService = siteSettingService;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var settings = await this._siteSettingService.GetAllAsync();

        // Always hide internal system settings (e.g. migration sentinel)
        settings = settings.Where(s => !InternalKeys.Contains(s.Key));

        // Non-admin users only see non-sensitive settings
        if (!this.IsAdmin)
        {
            settings = settings.Where(s => !SensitiveKeys.Contains(s.Key));
        }

        return this.Ok(settings.ToList());
    }

    [AllowAnonymous]
    [EnableRateLimiting("auth-read")]
    [HttpGet("public")]
    public async Task<IActionResult> GetPublic()
    {
        var publicSettings = await this._siteSettingService.GetPublicAsync();
        return this.Ok(publicSettings);
    }

    [HttpGet("discord-config")]
    public IActionResult GetDiscordConfig()
    {
        if (!this.IsAdmin)
        {
            return this.Forbid();
        }

        return this.Ok(new
        {
            clientId = MaskValue(this._discordSettings.ClientId),
            clientSecret = MaskSecret(this._discordSettings.ClientSecret),
            botToken = MaskSecret(this._discordSettings.BotToken),
            guildId = MaskValue(this._discordSettings.GuildId),
            geofenceForumChannelId = MaskValue(this._discordSettings.GeofenceForumChannelId),
            adminIds = MaskValue(this._poracleSettings.AdminIds),
        });
    }

    [HttpGet("telegram-config")]
    public IActionResult GetTelegramConfig()
    {
        if (!this.IsAdmin)
        {
            return this.Forbid();
        }

        return this.Ok(new
        {
            botToken = MaskSecret(this._telegramSettings.BotToken),
            botUsername = this._telegramSettings.BotUsername,
            enabled = this._telegramSettings.Enabled,
        });
    }

    [HttpPut("{key}")]
    public async Task<IActionResult> Upsert(string key, [FromBody] SiteSettingRequest request)
    {
        if (!this.IsAdmin)
        {
            return this.Forbid();
        }

        if (InternalKeys.Contains(key))
        {
            return this.BadRequest(new
            {
                error = "Cannot modify internal system settings."
            });
        }

        // Prevent lockout: at least one login method must remain enabled.
        // Uses GetValueAsync so absent/null = enabled (safe default). Only blocks when
        // both are explicitly "False".
        if (string.Equals(request.Value, "false", StringComparison.OrdinalIgnoreCase))
        {
            string? otherKey = key switch
            {
                EnableDiscordKey => EnableTelegramKey,
                EnableTelegramKey => EnableDiscordKey,
                _ => null
            };

            if (otherKey is not null)
            {
                var otherValue = await this._siteSettingService.GetValueAsync(otherKey);
                if (string.Equals(otherValue, "false", StringComparison.OrdinalIgnoreCase))
                {
                    return this.BadRequest(new
                    {
                        error = "At least one login method must remain enabled. Enable the other method first."
                    });
                }
            }
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
        public string? Value
        {
            get; set;
        }
        public string? Category
        {
            get; set;
        }
        public string? ValueType
        {
            get; set;
        }
    }

    /// <summary>
    /// Masks a non-secret value: shows first 4 and last 4 characters.
    /// Returns empty string if not configured.
    /// </summary>
    private static string MaskValue(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (value.Length <= 8)
        {
            return value;
        }

        return $"{value[..4]}{"".PadRight(value.Length - 8, '\u2022')}{value[^4..]}";
    }

    /// <summary>
    /// Masks a secret value: shows only last 4 characters.
    /// Returns empty string if not configured.
    /// </summary>
    private static string MaskSecret(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (value.Length <= 4)
        {
            return new string('\u2022', value.Length);
        }

        return $"{"".PadRight(value.Length - 4, '\u2022')}{value[^4..]}";
    }
}
