using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Pgan.PoracleWebNet.Api.Configuration;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Api.Controllers;

[Route("api/settings")]
public partial class SettingsController(
    ISiteSettingService siteSettingService,
    IOptions<DiscordSettings> discordSettings,
    IOptions<PoracleSettings> poracleSettings,
    IOptions<TelegramSettings> telegramSettings,
    IOptions<OidcSettings> oidcSettings,
    IUpstreamFeatureFlagService upstreamFlags,
    ICostumeCapabilityService costumeCapability,
    IConfiguration configuration,
    IPoracleApiProxy poracleApiProxy,
    IMemoryCache cache,
    ILogger<SettingsController> logger) : BaseApiController
{
    /// <summary>
    /// Exact setting keys a non-admin may read. This is an <em>allowlist</em>, deliberately: the previous
    /// denylist listed <c>scan_db</c>, which matches no real key (the rows are <c>scan_dbhost</c>,
    /// <c>scan_dbuser</c>, <c>scan_dbpass</c>, ...) and omitted <c>cf_id</c>/<c>cf_secret</c> entirely, so a
    /// scanner-database password and a Cloudflare Access token were served to every authenticated session.
    /// With an allowlist a newly added credential-bearing key is hidden by default instead of exposed.
    /// Admins still receive everything.
    /// </summary>
    private static readonly HashSet<string> UserVisibleKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "allowed_languages", "custom_title", "favicon_url", "header_logo_url",
        "hide_header_logo", "signup_url", "site_name",
        // The custom nav link is public branding, not a credential. Left off this list it reached admins
        // only -- the one group that least needs it -- so an admin configuring it saw it work and had no
        // way to tell it was invisible to everyone else. See #513.
        "custom_page_name", "custom_page_url", "custom_page_icon",
        // Poracle's own locale and its alert-language allow-list, synthesized rather than stored --
        // see GetPoracleProjectionsAsync.
        PoracleLocaleKey, PoracleAlertLanguagesKey,
    };

    /// <summary>
    /// Key families the SPA reads dynamically rather than by literal name: feature gates via
    /// <c>isDisabled(key)</c> / <c>disabledFeatureGuard</c>, and the uicons URL set. All are
    /// booleans or public asset URLs.
    /// </summary>
    private static readonly string[] UserVisibleKeyPrefixes = ["disable_", "enable_", "uicons_"];

    private static readonly HashSet<string> InternalKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "migration_completed",
    };

    private const string EnableDiscordKey = "enable_discord";
    private const string EnableTelegramKey = "enable_telegram";

    /// <summary>
    /// Pseudo-setting carrying Poracle's configured <c>locale</c>. It is not an admin-editable row: it is
    /// read from Poracle's config and appended to the settings response so the SPA can use it as the last
    /// language fallback ahead of the hardcoded <c>en</c>.
    /// </summary>
    internal const string PoracleLocaleKey = "poracle_locale";

    /// <summary>
    /// Pseudo-setting carrying the language codes Poracle will accept for a human's <em>alert</em>
    /// language, comma-separated, from <c>availableLanguages</c> on <c>GET /api/config/poracleWeb</c>.
    /// </summary>
    /// <remarks>
    /// Absent when Poracle restricts nothing — which covers both an unrestricted 5.2.1 and any server
    /// too old to report the field — so the SPA reads an absent value as "offer everything". Nothing to
    /// do with <c>allowed_languages</c>, which is this site's own restriction on the <em>display</em>
    /// language; the two govern different menus and neither substitutes for the other.
    /// </remarks>
    internal const string PoracleAlertLanguagesKey = "poracle_alert_languages";

    private const string PoracleProjectionsCacheKey = "settings:poracle_projections";

    /// <summary>Matches the shape of a locale tag (<c>de</c>, <c>pt-BR</c>, <c>zh-cn</c>) and nothing else.</summary>
    [GeneratedRegex("^[a-zA-Z]{2,3}(-[a-zA-Z0-9]{2,8})?$")]
    private static partial Regex LocalePattern();

    private readonly DiscordSettings _discordSettings = discordSettings.Value;
    private readonly PoracleSettings _poracleSettings = poracleSettings.Value;
    private readonly TelegramSettings _telegramSettings = telegramSettings.Value;
    private readonly OidcSettings _oidcSettings = oidcSettings.Value;
    private readonly ISiteSettingService _siteSettingService = siteSettingService;
    private readonly IUpstreamFeatureFlagService _upstreamFlags = upstreamFlags;
    private readonly IPoracleApiProxy _poracleApiProxy = poracleApiProxy;
    private readonly IMemoryCache _cache = cache;
    private readonly ICostumeCapabilityService _costumeCapability = costumeCapability;
    private readonly ILogger<SettingsController> _logger = logger;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var settings = await this._siteSettingService.GetAllAsync();

        // Always hide internal system settings (e.g. migration sentinel)
        settings = settings.Where(s => !InternalKeys.Contains(s.Key));

        // Non-admins see only the allowlisted keys the SPA actually needs.
        if (!this.IsAdmin)
        {
            settings = settings.Where(s => IsUserVisible(s.Key));
        }

        return this.Ok(await this.WithPoracleProjectionsAsync(settings));
    }

    /// <summary>True when a non-admin may read <paramref name="key"/>.</summary>
    internal static bool IsUserVisible(string? key) =>
        !string.IsNullOrWhiteSpace(key)
        && (UserVisibleKeys.Contains(key)
            || Array.Exists(UserVisibleKeyPrefixes, p => key.StartsWith(p, StringComparison.OrdinalIgnoreCase)));

    /// <summary>
    /// The <c>disable_*</c> keys the upstream Poracle deployment forces off in its own config, on top
    /// of whatever the site settings say. Lets the SPA hide those sections and the admin page mark the
    /// matching toggle as not-ours-to-change, instead of showing a switch that reads "enabled" while
    /// every write 403s.
    /// </summary>
    /// <remarks>
    /// Open to any signed-in user, not just admins: the same information is already obtainable by
    /// POSTing an alarm and reading the <c>disableKey</c> off the 403, and every non-admin consumer
    /// (nav, route guards) needs it. Empty when Poracle is unreachable or too old to report the flags.
    /// </remarks>
    [HttpGet("upstream-disabled")]
    public async Task<IActionResult> GetUpstreamDisabled()
    {
        var keys = await this._upstreamFlags.GetDisabledKeysAsync();
        return this.Ok(keys.OrderBy(k => k, StringComparer.Ordinal).ToList());
    }

    /// <summary>
    /// Whether this deployment's PoracleNG can store the costume filter, per alarm type.
    /// </summary>
    /// <remarks>
    /// Authenticated but not admin-only: the control it gates is an ordinary user control, and the
    /// pokemon and raid dialogs have to know before an admin ever looks at the server-profile page.
    /// Both false when PoracleNG is unreachable or predates the columns, which the caller must not be
    /// able to tell apart. The alarm services refuse an unsupported costume independently, so this says
    /// what to render, never what is allowed.
    /// </remarks>
    [HttpGet("costume-capability")]
    public async Task<IActionResult> GetCostumeCapability()
    {
        var capability = await this._costumeCapability.GetAsync(this.HttpContext.RequestAborted);

        return this.Ok(new
        {
            pokemon = capability.Pokemon,
            raid = capability.Raid,
        });
    }

    [AllowAnonymous]
    [EnableRateLimiting("auth-read")]
    [HttpGet("public")]
    public async Task<IActionResult> GetPublic()
    {
        var publicSettings = await this._siteSettingService.GetPublicAsync();
        return this.Ok(await this.WithPoracleProjectionsAsync(publicSettings));
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

    /// <summary>
    /// Returns the server-side OIDC provider configuration (env / appsettings) for the admin
    /// settings UI to display read-only. Secrets are masked; the client secret is never returned
    /// in full. <c>configured</c> reflects whether the full provider config is present, and
    /// <c>forceLocal</c> surfaces the AUTH_FORCE_LOCAL break-glass so the UI can explain why
    /// OIDC may be inactive even when enabled.
    /// </summary>
    [HttpGet("oidc-config")]
    public IActionResult GetOidcConfig()
    {
        if (!this.IsAdmin)
        {
            return this.Forbid();
        }

        var configured = !string.IsNullOrEmpty(this._oidcSettings.ClientId)
            && !string.IsNullOrEmpty(this._oidcSettings.AuthorizationUrl)
            && !string.IsNullOrEmpty(this._oidcSettings.TokenUrl)
            && !string.IsNullOrEmpty(this._oidcSettings.UserInfoUrl);

        return this.Ok(new
        {
            configured,
            enabled = this._oidcSettings.Enabled,
            forceLocal = configuration.GetValue<bool>("Auth:ForceLocal"),
            providerName = this._oidcSettings.ProviderName,
            authorizationUrl = this._oidcSettings.AuthorizationUrl,
            tokenUrl = this._oidcSettings.TokenUrl,
            userInfoUrl = this._oidcSettings.UserInfoUrl,
            endSessionUrl = this._oidcSettings.EndSessionUrl,
            clientId = MaskValue(this._oidcSettings.ClientId),
            clientSecret = MaskSecret(this._oidcSettings.ClientSecret),
            scopes = this._oidcSettings.Scopes,
            identityClaim = this._oidcSettings.IdentityClaim,
            usePkce = this._oidcSettings.UsePkce,
            // Refresh-token consumption (server-side config only — controlled by OIDC_USE_REFRESH_TOKENS;
            // there is no runtime admin toggle, as refresh is coupled to the per-login JWT lifetime).
            useRefreshTokens = this._oidcSettings.UseRefreshTokens,
            accessTokenMinutes = this._oidcSettings.AccessTokenMinutes,
            refreshTokenLifetimeDays = this._oidcSettings.RefreshTokenLifetimeDays,
            revokedRetentionDays = this._oidcSettings.RevokedRetentionDays,
            offlineAccessScope = this._oidcSettings.OfflineAccessScope,
            tokenEndpointAuthMethod = this._oidcSettings.TokenEndpointAuthMethod,
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

        // Both of these are projections of Poracle's config, not rows this page owns. Nothing stopped
        // them being written, and because a real row wins over the synthesized value, one accidental save
        // would have pinned the language default forever and silently stopped tracking Poracle. See #780.
        if (string.Equals(key, PoracleLocaleKey, StringComparison.OrdinalIgnoreCase)
            || string.Equals(key, PoracleAlertLanguagesKey, StringComparison.OrdinalIgnoreCase))
        {
            return this.BadRequest(new
            {
                error = $"{key} is read from Poracle's configuration and cannot be set here."
            });
        }

        // Prevent lockout: at least one login method must remain enabled.
        // Uses GetValueAsync so absent/null = enabled (safe default). Only blocks when
        // both are explicitly "False".
        if (string.Equals(request.Value, "false", StringComparison.OrdinalIgnoreCase))
        {
            var otherKey = key switch
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

    /// <summary>
    /// Appends the two Poracle pseudo-settings to <paramref name="settings"/>, each unless a real row of
    /// the same key already exists -- an admin-set value wins over what Poracle reports.
    /// </summary>
    private async Task<List<SiteSetting>> WithPoracleProjectionsAsync(IEnumerable<SiteSetting> settings)
    {
        var list = settings.ToList();
        var (locale, alertLanguages) = await this.GetPoracleProjectionsAsync();

        Project(list, PoracleLocaleKey, locale);
        Project(list, PoracleAlertLanguagesKey, alertLanguages);

        return list;
    }

    private static void Project(List<SiteSetting> list, string key, string? value)
    {
        if (string.IsNullOrEmpty(value)
            || list.Exists(s => string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        list.Add(new SiteSetting
        {
            Key = key,
            Value = value,
            Category = "branding",
            ValueType = "string",
        });
    }

    /// <summary>
    /// Reads Poracle's <c>locale</c> and its alert-language allow-list from one config call, cached for
    /// five minutes. Both the settings endpoints that serve them are hit on every page load, and one of
    /// them is anonymous, so an uncached read would put a PoracleNG roundtrip in front of the login page.
    /// A Poracle outage caches two nulls: the SPA keeps its existing stored/browser/<c>en</c> ordering
    /// and offers the full alert-language menu, both of which are what an unrestricted server would give
    /// anyway. Neither value is ever a blocker.
    /// </summary>
    private async Task<(string? Locale, string? AlertLanguages)> GetPoracleProjectionsAsync()
    {
        if (this._cache.TryGetValue<(string?, string?)>(PoracleProjectionsCacheKey, out var cached))
        {
            return cached;
        }

        (string? Locale, string? AlertLanguages) projections = (null, null);
        try
        {
            var config = await this._poracleApiProxy.GetConfigAsync();
            projections = (NormalizeLocale(config?.Locale), NormalizeAlertLanguages(config?.AvailableLanguages));
        }
        catch (Exception ex)
        {
            LogFetchLocaleFailed(this._logger, ex);
        }

        this._cache.Set(PoracleProjectionsCacheKey, projections, TimeSpan.FromMinutes(5));
        return projections;
    }

    /// <summary>
    /// Renders Poracle's <c>availableLanguages</c> as a comma-separated list, or null when it restricts
    /// nothing. Null upstream means unrestricted -- an unset and an empty map both report it, because
    /// Poracle's own write path only validates a non-empty one -- and so does an absent field, which is
    /// what a server older than 5.2.1 sends. All three are the same answer here: no row, full menu.
    /// Individual codes are shape-checked and dropped rather than the whole list being discarded.
    /// </summary>
    internal static string? NormalizeAlertLanguages(IEnumerable<string>? availableLanguages)
    {
        if (availableLanguages is null)
        {
            return null;
        }

        var codes = availableLanguages
            .Select(NormalizeLocale)
            .Where(c => !string.IsNullOrEmpty(c))
            .ToList();

        return codes.Count == 0 ? null : string.Join(',', codes);
    }

    /// <summary>
    /// Returns <paramref name="locale"/> when it looks like a locale tag, otherwise null. Deliberately a
    /// shape check rather than a list of the eleven languages this UI ships: the SPA does that matching
    /// itself against its own language list and the <c>allowed_languages</c> filter, and a locale it cannot
    /// place simply loses to <c>en</c>. An allowlist here would need updating every time a translation lands.
    /// </summary>
    internal static string? NormalizeLocale(string? locale)
    {
        var trimmed = locale?.Trim();
        return !string.IsNullOrEmpty(trimmed) && LocalePattern().IsMatch(trimmed) ? trimmed : null;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to read Poracle's configuration for the settings projections")]
    private static partial void LogFetchLocaleFailed(ILogger logger, Exception ex);

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
