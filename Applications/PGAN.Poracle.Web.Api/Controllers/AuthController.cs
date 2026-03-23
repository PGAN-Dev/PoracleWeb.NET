using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PGAN.Poracle.Web.Api.Configuration;
using PGAN.Poracle.Web.Core.Abstractions.Services;
using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Api.Controllers;

[Route("api/auth")]
[EnableRateLimiting("auth")]
public partial class AuthController(
    IHumanService humanService,
    IPoracleApiProxy poracleApiProxy,
    IPwebSettingService pwebSettingService,
    IOptions<JwtSettings> jwtSettings,
    IOptions<DiscordSettings> discordSettings,
    IOptions<TelegramSettings> telegramSettings,
    IOptions<PoracleSettings> poracleSettings,
    ILogger<AuthController> logger) : BaseApiController
{
    private readonly IHumanService _humanService = humanService;
    private readonly IPoracleApiProxy _poracleApiProxy = poracleApiProxy;
    private readonly IPwebSettingService _pwebSettingService = pwebSettingService;
    private readonly JwtSettings _jwtSettings = jwtSettings.Value;
    private readonly DiscordSettings _discordSettings = discordSettings.Value;
    private readonly TelegramSettings _telegramSettings = telegramSettings.Value;
    private readonly PoracleSettings _poracleSettings = poracleSettings.Value;
    private readonly ILogger<AuthController> _logger = logger;

    [AllowAnonymous]
    [HttpGet("discord/login")]
    public IActionResult DiscordLogin()
    {
        // Generate a random state value for CSRF protection
        var state = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        var isHttps = string.Equals(this.Request.Scheme, "https", StringComparison.OrdinalIgnoreCase);
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = isHttps,
            SameSite = SameSiteMode.Lax,
            MaxAge = TimeSpan.FromMinutes(10)
        };

        this.Response.Cookies.Append("oauth_state", state, cookieOptions);

        // Save the frontend origin so we know where to redirect after the callback.
        // Validate against configured CORS origins to prevent open redirect token theft.
        var allowedOrigins = this.HttpContext.RequestServices
            .GetRequiredService<IConfiguration>()
            .GetSection("Cors:AllowedOrigins").Get<string[]>();
        var selfOrigin = $"{this.Request.Scheme}://{this.Request.Host}";
        var origin = selfOrigin;

        var referer = this.Request.Headers.Referer.FirstOrDefault();
        if (!string.IsNullOrEmpty(referer) && Uri.TryCreate(referer, UriKind.Absolute, out var refererUri))
        {
            var refererOrigin = $"{refererUri.Scheme}://{refererUri.Authority}";
            if (allowedOrigins is { Length: > 0 }
                ? allowedOrigins.Any(o => string.Equals(o, refererOrigin, StringComparison.OrdinalIgnoreCase))
                : string.Equals(refererOrigin, selfOrigin, StringComparison.OrdinalIgnoreCase))
            {
                origin = refererOrigin;
            }
        }

        this.Response.Cookies.Append("oauth_origin", origin, cookieOptions);

        // Redirect URI points to the API itself, not the Angular app
        var callbackUri = $"{this.Request.Scheme}://{this.Request.Host}/api/auth/discord/callback";
        var redirectUrl = "https://discordapp.com/api/oauth2/authorize" +
            $"?client_id={this._discordSettings.ClientId}" +
            $"&redirect_uri={Uri.EscapeDataString(callbackUri)}" +
            "&response_type=code" +
            "&scope=identify" +
            $"&state={Uri.EscapeDataString(state)}";

        return this.Redirect(redirectUrl);
    }

    [AllowAnonymous]
    [HttpGet("discord/callback")]
    public async Task<IActionResult> DiscordCallback([FromQuery] string code, [FromQuery] string? state)
    {
        // Derive frontend URL from the request
        var frontendUrl = this.GetFrontendUrl();

        // Validate OAuth state parameter for CSRF protection
        var savedState = this.Request.Cookies["oauth_state"];
        this.Response.Cookies.Delete("oauth_state");

        if (string.IsNullOrEmpty(state) || string.IsNullOrEmpty(savedState) || state != savedState)
        {
            return this.BadRequest(new
            {
                error = "Invalid OAuth state. Possible CSRF attack."
            });
        }

        if (string.IsNullOrEmpty(code))
        {
            return this.Redirect($"{frontendUrl}/login#error=missing_code");
        }

        using var httpClient = new HttpClient();

        // Exchange code for access token
        var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = this._discordSettings.ClientId,
            ["client_secret"] = this._discordSettings.ClientSecret,
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = $"{this.Request.Scheme}://{this.Request.Host}/api/auth/discord/callback"
        });

        var tokenResponse = await httpClient.PostAsync("https://discordapp.com/api/oauth2/token", tokenRequest);
        if (!tokenResponse.IsSuccessStatusCode)
        {
            var errorBody = await tokenResponse.Content.ReadAsStringAsync();
            LogDiscordTokenExchangeFailed(this._logger, tokenResponse.StatusCode, errorBody);
            return this.Redirect($"{frontendUrl}/login#error=token_exchange_failed");
        }

        var tokenJson = await tokenResponse.Content.ReadFromJsonAsync<JsonElement>();
        var accessToken = tokenJson.GetProperty("access_token").GetString();

        // Get Discord user info
        httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        var userResponse = await httpClient.GetAsync("https://discordapp.com/api/users/@me");
        if (!userResponse.IsSuccessStatusCode)
        {
            return this.Redirect($"{frontendUrl}/login#error=discord_user_fetch_failed");
        }

        var discordUser = await userResponse.Content.ReadFromJsonAsync<JsonElement>();
        var discordId = discordUser.GetProperty("id").GetString()!;
        var username = discordUser.GetProperty("username").GetString()!;
        var avatar = discordUser.TryGetProperty("avatar", out var avatarProp) ? avatarProp.GetString() : null;
        var avatarUrl = avatar != null
            ? $"https://cdn.discordapp.com/avatars/{discordId}/{avatar}.png"
            : null;

        // Look up user in DB
        var human = await this._humanService.GetByIdAsync(discordId);
        if (human == null)
        {
            return this.Redirect($"{frontendUrl}/login#error=user_not_registered");
        }

        // Role-based access: check Discord guild roles if enabled
        var (isAdmin, managedWebhooks) = await this.GetRolesAsync(discordId);
        if (!isAdmin)
        {
            var roleCheckResult = await this.CheckRoleAccessAsync(discordId);
            if (roleCheckResult != null)
            {
                return this.Redirect($"{frontendUrl}/login#error={roleCheckResult}");
            }
        }

        var userInfo = new UserInfo
        {
            Id = discordId,
            Username = username,
            Type = "discord:user",
            IsAdmin = isAdmin,
            Enabled = human.Enabled == 1 && human.AdminDisable == 0,
            ProfileNo = human.CurrentProfileNo,
            AvatarUrl = avatarUrl,
            ManagedWebhooks = managedWebhooks
        };

        // Cache avatar for admin panel use
        if (!string.IsNullOrEmpty(avatarUrl))
        {
            Services.AvatarCacheService.SetAvatar(discordId, avatarUrl);
            Services.AvatarCacheService.Save();
        }

        var jwt = this.GenerateJwtToken(userInfo);

        // Redirect browser to Angular with token in URL fragment to avoid server-side leakage
        return this.Redirect($"{frontendUrl}/auth/discord/callback#token={jwt}");
    }

    [AllowAnonymous]
    [HttpPost("telegram/verify")]
    public async Task<IActionResult> TelegramVerify([FromBody] Dictionary<string, string> telegramData)
    {
        if (telegramData == null || !telegramData.TryGetValue("id", out var telegramId) || !telegramData.TryGetValue("hash", out var hash))
        {
            return this.BadRequest(new
            {
                error = "Invalid Telegram login data."
            });
        }

        if (!this._telegramSettings.Enabled)
        {
            return this.BadRequest(new
            {
                error = "Telegram authentication is not enabled."
            });
        }

        // Validate auth_date is not older than 86400 seconds (24 hours)
        if (telegramData.TryGetValue("auth_date", out var authDateStr) &&
            long.TryParse(authDateStr, out var authDateUnix))
        {
            var authDate = DateTimeOffset.FromUnixTimeSeconds(authDateUnix);
            if (DateTimeOffset.UtcNow - authDate > TimeSpan.FromSeconds(86400))
            {
                return this.Unauthorized(new
                {
                    error = "Telegram authentication data has expired."
                });
            }
        }
        else
        {
            return this.BadRequest(new
            {
                error = "Missing or invalid auth_date."
            });
        }

        // Validate HMAC-SHA256
        var botToken = this._telegramSettings.BotToken;

        var dataCheckString = string.Join("\n",
            telegramData
                .Where(kvp => kvp.Key != "hash")
                .OrderBy(kvp => kvp.Key)
                .Select(kvp => $"{kvp.Key}={kvp.Value}"));
        var secretKey = SHA256.HashData(Encoding.UTF8.GetBytes(botToken));

        using var hmac = new HMACSHA256(secretKey);
        var computedHash = Convert.ToHexStringLower(hmac.ComputeHash(Encoding.UTF8.GetBytes(dataCheckString)));

        if (computedHash != hash)
        {
            return this.Unauthorized(new
            {
                error = "Invalid Telegram authentication data."
            });
        }

        var username = telegramData.GetValueOrDefault("username",
            telegramData.GetValueOrDefault("first_name", "Unknown"));
        var photoUrl = telegramData.GetValueOrDefault("photo_url");

        // Look up user in DB
        var human = await this._humanService.GetByIdAsync(telegramId);
        if (human == null)
        {
            return this.StatusCode(403, new
            {
                error = "User not registered in Poracle."
            });
        }

        var (isAdmin, managedWebhooks) = await this.GetRolesAsync(telegramId);

        var userInfo = new UserInfo
        {
            Id = telegramId,
            Username = username!,
            Type = "telegram:user",
            IsAdmin = isAdmin,
            Enabled = human.Enabled == 1 && human.AdminDisable == 0,
            ProfileNo = human.CurrentProfileNo,
            AvatarUrl = photoUrl,
            ManagedWebhooks = managedWebhooks
        };

        var jwt = this.GenerateJwtToken(userInfo);

        return this.Ok(new
        {
            token = jwt,
            user = userInfo
        });
    }

    [AllowAnonymous]
    [HttpGet("telegram/config")]
    public IActionResult TelegramConfig() => this.Ok(new
    {
        enabled = this._telegramSettings.Enabled,
        botUsername = this._telegramSettings.BotUsername
    });

    [EnableRateLimiting("auth-read")]
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        // Read enabled status from DB (not JWT) so it reflects real-time changes
        var human = await this._humanService.GetByIdAsync(this.UserId);
        var enabled = human == null || (human.Enabled == 1 && human.AdminDisable == 0);

        var userInfo = new UserInfo
        {
            Id = this.UserId,
            Username = this.Username,
            Type = this.User.FindFirstValue("type") ?? string.Empty,
            IsAdmin = this.IsAdmin,
            Enabled = enabled,
            ProfileNo = human?.CurrentProfileNo ?? this.ProfileNo,
            AvatarUrl = this.User.FindFirstValue("avatarUrl"),
            ManagedWebhooks = this.ManagedWebhooks.Length > 0 ? this.ManagedWebhooks : null
        };

        return this.Ok(userInfo);
    }

    [EnableRateLimiting("auth-read")]
    [HttpPost("alerts/toggle")]
    public async Task<IActionResult> ToggleAlerts()
    {
        var human = await this._humanService.GetByIdAsync(this.UserId);
        if (human == null)
        {
            return this.NotFound();
        }

        if (human.AdminDisable == 1)
        {
            return this.BadRequest(new
            {
                error = "Your account has been disabled by an administrator."
            });
        }

        human.Enabled = human.Enabled == 1 ? 0 : 1;
        await this._humanService.UpdateAsync(human);
        return this.Ok(new
        {
            enabled = human.Enabled == 1
        });
    }

    [EnableRateLimiting("auth-read")]
    [HttpPost("logout")]
    public IActionResult Logout() => this.Ok(new
    {
        message = "Logged out successfully."
    });

    private string GenerateJwtToken(UserInfo user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(this._jwtSettings.Secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new("userId", user.Id),
            new("username", user.Username),
            new("type", user.Type),
            new("isAdmin", user.IsAdmin.ToString().ToLowerInvariant()),
            new("enabled", user.Enabled.ToString().ToLowerInvariant()),
            new("profileNo", user.ProfileNo.ToString(CultureInfo.InvariantCulture)),
        };

        if (!string.IsNullOrEmpty(user.AvatarUrl))
        {
            claims.Add(new Claim("avatarUrl", user.AvatarUrl));
        }

        if (user.ManagedWebhooks is { Length: > 0 })
        {
            claims.Add(new Claim("managedWebhooks", string.Join(',', user.ManagedWebhooks)));
        }

        var token = new JwtSecurityToken(
            issuer: this._jwtSettings.Issuer,
            audience: this._jwtSettings.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(this._jwtSettings.ExpirationMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// Calls PoracleJS getAdministrationRoles once and returns (isAdmin, managedWebhooks).
    /// managedWebhooks merges: Poracle-resolved webhook delegation + our own pweb_settings layer.
    /// </summary>
    private async Task<(bool isAdmin, string[]? managedWebhooks)> GetRolesAsync(string userId)
    {
        // Fast path: configured admin IDs
        if (!string.IsNullOrEmpty(this._poracleSettings.AdminIds))
        {
            var adminIds = this._poracleSettings.AdminIds.Split(',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (adminIds.Contains(userId))
            {
                return (true, null);
            }
        }

        // Check Poracle config admins list
        try
        {
            var config = await this._poracleApiProxy.GetConfigAsync();
            if (config?.Admins != null &&
                (config.Admins.Discord.Contains(userId) || config.Admins.Telegram.Contains(userId)))
            {
                return (true, null);
            }
        }
        catch (Exception ex)
        {
            LogPoracleConfigFetchFailed(this._logger, ex, userId);
        }

        // Call getAdministrationRoles once — resolves delegation including Discord guild roles
        var managed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var isAdmin = false;

        try
        {
            var rolesJson = await this._poracleApiProxy.GetAdminRolesAsync(userId);
            if (!string.IsNullOrEmpty(rolesJson))
            {
                using var doc = JsonDocument.Parse(rolesJson);
                var root = doc.RootElement;

                // Some versions return isAdmin at root; others wrap under admin.discord
                if (root.TryGetProperty("isAdmin", out var isAdminProp) && isAdminProp.ValueKind == JsonValueKind.True)
                {
                    isAdmin = true;
                }

                // Parse admin.discord.webhooks — the authoritative delegate webhook list
                if (root.TryGetProperty("admin", out var adminEl) &&
                    adminEl.TryGetProperty("discord", out var discordEl))
                {
                    if (!isAdmin &&
                        discordEl.TryGetProperty("isAdmin", out var discordAdmin) &&
                        discordAdmin.ValueKind == JsonValueKind.True)
                    {
                        isAdmin = true;
                    }

                    if (discordEl.TryGetProperty("webhooks", out var webhooks) &&
                        webhooks.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var wh in webhooks.EnumerateArray())
                        {
                            if (wh.GetString() is { } id)
                            {
                                managed.Add(id);
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogAdminRolesFetchFailed(this._logger, ex, userId);
        }

        if (isAdmin)
        {
            return (true, null);
        }

        // Also merge our own pweb_settings delegation layer
        try
        {
            var allSettings = await this._pwebSettingService.GetAllAsync();
            const string prefix = "webhook_delegates:";
            foreach (var setting in allSettings)
            {
                if (setting.Setting?.StartsWith(prefix, StringComparison.Ordinal) == true)
                {
                    var delegates = setting.Value?.Split(',',
                        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
                    if (delegates.Contains(userId))
                    {
                        managed.Add(setting.Setting[prefix.Length..]);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogPwebDelegatesFetchFailed(this._logger, ex, userId);
        }

        return (false, managed.Count > 0 ? managed.ToArray() : null);
    }

    /// <summary>
    /// Checks if role-based access is enabled and whether the user has an allowed role.
    /// Returns null if access is granted, or an error string if denied.
    /// </summary>
    private async Task<string?> CheckRoleAccessAsync(string discordId)
    {
        try
        {
            var allSettings = await this._pwebSettingService.GetAllAsync();
            var settingsMap = allSettings.ToDictionary(s => s.Setting ?? "", s => s.Value ?? "");

            // Check if role-based access is enabled
            if (!settingsMap.TryGetValue("enable_roles", out var enableRoles) ||
                !string.Equals(enableRoles, "True", StringComparison.OrdinalIgnoreCase))
            {
                return null; // Not enabled, allow access
            }

            // Get allowed role IDs
            if (!settingsMap.TryGetValue("allowed_role_ids", out var roleIdsStr) ||
                string.IsNullOrWhiteSpace(roleIdsStr))
            {
                return null; // No roles configured, allow everyone
            }

            var allowedRoles = new HashSet<string>(
                roleIdsStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

            // Need bot token and guild ID to check roles
            if (string.IsNullOrEmpty(this._discordSettings.BotToken) || string.IsNullOrEmpty(this._discordSettings.GuildId))
            {
                LogRoleMisconfigured(this._logger);
                return null; // Misconfigured, fail open
            }

            // Fetch user's guild member data via bot API
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bot", this._discordSettings.BotToken);

            var response = await httpClient.GetAsync(
                $"https://discordapp.com/api/guilds/{this._discordSettings.GuildId}/members/{discordId}");

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                LogGuildMemberFetchFailed(this._logger, discordId, response.StatusCode, errorBody);
                // 404 = not in guild, 403 = bot doesn't have access
                return response.StatusCode == System.Net.HttpStatusCode.NotFound
                    ? "not_in_guild"
                    : "role_check_failed";
            }

            var memberJson = await response.Content.ReadFromJsonAsync<JsonElement>();
            var userRoles = new HashSet<string>();
            if (memberJson.TryGetProperty("roles", out var rolesArray) &&
                rolesArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var role in rolesArray.EnumerateArray())
                {
                    if (role.GetString() is { } roleId)
                    {
                        userRoles.Add(roleId);
                    }
                }
            }

            if (this._logger.IsEnabled(LogLevel.Information))
            {
#pragma warning disable CA1873 // args are only evaluated inside IsEnabled guard
                LogRoleCheck(this._logger, discordId, string.Join(", ", allowedRoles), string.Join(", ", userRoles));
#pragma warning restore CA1873
            }

            // User must have ALL of the allowed roles
            if (allowedRoles.IsSubsetOf(userRoles))
            {
                return null;
            }

            LogRoleDenied(this._logger, discordId);
            return "missing_required_role";
        }
        catch (Exception ex)
        {
            LogRoleCheckFailed(this._logger, ex, discordId);
            return "role_check_failed";
        }
    }

    private string GetFrontendUrl()
    {
        // Use the origin saved during the login step
        var savedOrigin = this.Request.Cookies["oauth_origin"];
        this.Response.Cookies.Delete("oauth_origin");
        if (!string.IsNullOrEmpty(savedOrigin))
        {
            return savedOrigin.TrimEnd('/');
        }

        // Fallback: same scheme/host as the request
        return $"{this.Request.Scheme}://{this.Request.Host}";
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Role-based access enabled but Discord BotToken or GuildId not configured.")]
    private static partial void LogRoleMisconfigured(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to fetch guild member {UserId}: {Status} {Body}")]
    private static partial void LogGuildMemberFetchFailed(ILogger logger, string userId, System.Net.HttpStatusCode status, string body);

    [LoggerMessage(Level = LogLevel.Information, Message = "Role check for {UserId}: required=[{Required}], user=[{UserRoles}]")]
    private static partial void LogRoleCheck(ILogger logger, string userId, string required, string userRoles);

    [LoggerMessage(Level = LogLevel.Information, Message = "User {UserId} denied: missing required roles.")]
    private static partial void LogRoleDenied(ILogger logger, string userId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Role check failed for {UserId}, denying access.")]
    private static partial void LogRoleCheckFailed(ILogger logger, Exception ex, string userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Discord token exchange failed: {Status} {Body}")]
    private static partial void LogDiscordTokenExchangeFailed(ILogger logger, System.Net.HttpStatusCode status, string body);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to fetch Poracle config for admin check for {UserId}.")]
    private static partial void LogPoracleConfigFetchFailed(ILogger logger, Exception ex, string userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to fetch administration roles for {UserId}.")]
    private static partial void LogAdminRolesFetchFailed(ILogger logger, Exception ex, string userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to fetch pweb_settings delegates for {UserId}.")]
    private static partial void LogPwebDelegatesFetchFailed(ILogger logger, Exception ex, string userId);
}
