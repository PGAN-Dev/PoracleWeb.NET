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
public class AuthController : BaseApiController
{
    private readonly IHumanService _humanService;
    private readonly IPoracleApiProxy _poracleApiProxy;
    private readonly IPwebSettingService _pwebSettingService;
    private readonly JwtSettings _jwtSettings;
    private readonly DiscordSettings _discordSettings;
    private readonly TelegramSettings _telegramSettings;
    private readonly PoracleSettings _poracleSettings;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        IHumanService humanService,
        IPoracleApiProxy poracleApiProxy,
        IPwebSettingService pwebSettingService,
        IOptions<JwtSettings> jwtSettings,
        IOptions<DiscordSettings> discordSettings,
        IOptions<TelegramSettings> telegramSettings,
        IOptions<PoracleSettings> poracleSettings,
        ILogger<AuthController> logger)
    {
        _humanService = humanService;
        _poracleApiProxy = poracleApiProxy;
        _pwebSettingService = pwebSettingService;
        _jwtSettings = jwtSettings.Value;
        _discordSettings = discordSettings.Value;
        _telegramSettings = telegramSettings.Value;
        _poracleSettings = poracleSettings.Value;
        _logger = logger;
    }

    [AllowAnonymous]
    [HttpGet("discord/login")]
    public IActionResult DiscordLogin()
    {
        // Generate a random state value for CSRF protection
        var state = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        var isHttps = string.Equals(Request.Scheme, "https", StringComparison.OrdinalIgnoreCase);
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = isHttps,
            SameSite = SameSiteMode.Lax,
            MaxAge = TimeSpan.FromMinutes(10)
        };

        Response.Cookies.Append("oauth_state", state, cookieOptions);

        // Save the frontend origin so we know where to redirect after the callback
        var referer = Request.Headers.Referer.FirstOrDefault();
        var origin = !string.IsNullOrEmpty(referer) && Uri.TryCreate(referer, UriKind.Absolute, out var refererUri)
            ? $"{refererUri.Scheme}://{refererUri.Authority}"
            : $"{Request.Scheme}://{Request.Host}";
        Response.Cookies.Append("oauth_origin", origin, cookieOptions);

        // Redirect URI points to the API itself, not the Angular app
        var callbackUri = $"{Request.Scheme}://{Request.Host}/api/auth/discord/callback";
        var redirectUrl = "https://discordapp.com/api/oauth2/authorize" +
            $"?client_id={_discordSettings.ClientId}" +
            $"&redirect_uri={Uri.EscapeDataString(callbackUri)}" +
            "&response_type=code" +
            "&scope=identify" +
            $"&state={Uri.EscapeDataString(state)}";

        return Redirect(redirectUrl);
    }

    [AllowAnonymous]
    [HttpGet("discord/callback")]
    public async Task<IActionResult> DiscordCallback([FromQuery] string code, [FromQuery] string? state)
    {
        // Derive frontend URL from the request
        var frontendUrl = GetFrontendUrl();

        // Validate OAuth state parameter for CSRF protection
        var savedState = Request.Cookies["oauth_state"];
        Response.Cookies.Delete("oauth_state");

        if (string.IsNullOrEmpty(state) || string.IsNullOrEmpty(savedState) || state != savedState)
            return BadRequest(new { error = "Invalid OAuth state. Possible CSRF attack." });

        if (string.IsNullOrEmpty(code))
            return Redirect($"{frontendUrl}/login?error=missing_code");

        using var httpClient = new HttpClient();

        // Exchange code for access token
        var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = _discordSettings.ClientId,
            ["client_secret"] = _discordSettings.ClientSecret,
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = $"{Request.Scheme}://{Request.Host}/api/auth/discord/callback"
        });

        var tokenResponse = await httpClient.PostAsync("https://discordapp.com/api/oauth2/token", tokenRequest);
        if (!tokenResponse.IsSuccessStatusCode)
        {
            var errorBody = await tokenResponse.Content.ReadAsStringAsync();
            _logger.LogWarning("Discord token exchange failed: {Status} {Body}", tokenResponse.StatusCode, errorBody);
            return Redirect($"{frontendUrl}/login?error=token_exchange_failed");
        }

        var tokenJson = await tokenResponse.Content.ReadFromJsonAsync<JsonElement>();
        var accessToken = tokenJson.GetProperty("access_token").GetString();

        // Get Discord user info
        httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        var userResponse = await httpClient.GetAsync("https://discordapp.com/api/users/@me");
        if (!userResponse.IsSuccessStatusCode)
            return Redirect($"{frontendUrl}/login?error=discord_user_fetch_failed");

        var discordUser = await userResponse.Content.ReadFromJsonAsync<JsonElement>();
        var discordId = discordUser.GetProperty("id").GetString()!;
        var username = discordUser.GetProperty("username").GetString()!;
        var avatar = discordUser.TryGetProperty("avatar", out var avatarProp) ? avatarProp.GetString() : null;
        var avatarUrl = avatar != null
            ? $"https://cdn.discordapp.com/avatars/{discordId}/{avatar}.png"
            : null;

        // Look up user in DB
        var human = await _humanService.GetByIdAsync(discordId);
        if (human == null)
            return Redirect($"{frontendUrl}/login?error=user_not_registered");

        // Role-based access: check Discord guild roles if enabled
        var (isAdmin, managedWebhooks) = await GetRolesAsync(discordId);
        if (!isAdmin)
        {
            var roleCheckResult = await CheckRoleAccessAsync(discordId);
            if (roleCheckResult != null)
                return Redirect($"{frontendUrl}/login?error={roleCheckResult}");
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

        var jwt = GenerateJwtToken(userInfo);

        // Redirect browser to Angular with token in URL fragment to avoid server-side leakage
        return Redirect($"{frontendUrl}/auth/discord/callback#token={jwt}");
    }

    [AllowAnonymous]
    [HttpPost("telegram/verify")]
    public async Task<IActionResult> TelegramVerify([FromBody] Dictionary<string, string> telegramData)
    {
        if (telegramData == null || !telegramData.ContainsKey("id") || !telegramData.ContainsKey("hash"))
            return BadRequest(new { error = "Invalid Telegram login data." });

        if (!_telegramSettings.Enabled)
            return BadRequest(new { error = "Telegram authentication is not enabled." });

        // Validate auth_date is not older than 86400 seconds (24 hours)
        if (telegramData.TryGetValue("auth_date", out var authDateStr) &&
            long.TryParse(authDateStr, out var authDateUnix))
        {
            var authDate = DateTimeOffset.FromUnixTimeSeconds(authDateUnix);
            if (DateTimeOffset.UtcNow - authDate > TimeSpan.FromSeconds(86400))
                return Unauthorized(new { error = "Telegram authentication data has expired." });
        }
        else
        {
            return BadRequest(new { error = "Missing or invalid auth_date." });
        }

        // Validate HMAC-SHA256
        var botToken = _telegramSettings.BotToken;
        var hash = telegramData["hash"];

        var dataCheckString = string.Join("\n",
            telegramData
                .Where(kvp => kvp.Key != "hash")
                .OrderBy(kvp => kvp.Key)
                .Select(kvp => $"{kvp.Key}={kvp.Value}"));

        using var sha256 = SHA256.Create();
        var secretKey = sha256.ComputeHash(Encoding.UTF8.GetBytes(botToken));

        using var hmac = new HMACSHA256(secretKey);
        var computedHash = BitConverter.ToString(
            hmac.ComputeHash(Encoding.UTF8.GetBytes(dataCheckString)))
            .Replace("-", "")
            .ToLowerInvariant();

        if (computedHash != hash)
            return Unauthorized(new { error = "Invalid Telegram authentication data." });

        var telegramId = telegramData["id"];
        var username = telegramData.GetValueOrDefault("username",
            telegramData.GetValueOrDefault("first_name", "Unknown"));
        var photoUrl = telegramData.GetValueOrDefault("photo_url");

        // Look up user in DB
        var human = await _humanService.GetByIdAsync(telegramId);
        if (human == null)
            return StatusCode(403, new { error = "User not registered in Poracle." });

        var (isAdmin, managedWebhooks) = await GetRolesAsync(telegramId);

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

        var jwt = GenerateJwtToken(userInfo);

        return Ok(new
        {
            token = jwt,
            user = userInfo
        });
    }

    [AllowAnonymous]
    [HttpGet("telegram/config")]
    public IActionResult TelegramConfig()
    {
        return Ok(new
        {
            enabled = _telegramSettings.Enabled,
            botUsername = _telegramSettings.BotUsername
        });
    }

    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        // Read enabled status from DB (not JWT) so it reflects real-time changes
        var human = await _humanService.GetByIdAsync(UserId);
        var enabled = human != null ? human.Enabled == 1 && human.AdminDisable == 0 : true;

        var userInfo = new UserInfo
        {
            Id = UserId,
            Username = Username,
            Type = User.FindFirstValue("type") ?? string.Empty,
            IsAdmin = IsAdmin,
            Enabled = enabled,
            ProfileNo = ProfileNo,
            AvatarUrl = User.FindFirstValue("avatarUrl"),
            ManagedWebhooks = ManagedWebhooks.Length > 0 ? ManagedWebhooks : null
        };

        return Ok(userInfo);
    }

    [HttpPost("alerts/toggle")]
    public async Task<IActionResult> ToggleAlerts()
    {
        var human = await _humanService.GetByIdAsync(UserId);
        if (human == null) return NotFound();

        if (human.AdminDisable == 1)
            return BadRequest(new { error = "Your account has been disabled by an administrator." });

        human.Enabled = human.Enabled == 1 ? 0 : 1;
        await _humanService.UpdateAsync(human);
        return Ok(new { enabled = human.Enabled == 1 });
    }

    [HttpPost("logout")]
    public IActionResult Logout()
    {
        return Ok(new { message = "Logged out successfully." });
    }

    private string GenerateJwtToken(UserInfo user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.Secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new("userId", user.Id),
            new("username", user.Username),
            new("type", user.Type),
            new("isAdmin", user.IsAdmin.ToString().ToLowerInvariant()),
            new("enabled", user.Enabled.ToString().ToLowerInvariant()),
            new("profileNo", user.ProfileNo.ToString()),
        };

        if (!string.IsNullOrEmpty(user.AvatarUrl))
            claims.Add(new Claim("avatarUrl", user.AvatarUrl));

        if (user.ManagedWebhooks is { Length: > 0 })
            claims.Add(new Claim("managedWebhooks", string.Join(',', user.ManagedWebhooks)));

        var token = new JwtSecurityToken(
            issuer: _jwtSettings.Issuer,
            audience: _jwtSettings.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_jwtSettings.ExpirationMinutes),
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
        if (!string.IsNullOrEmpty(_poracleSettings.AdminIds))
        {
            var adminIds = _poracleSettings.AdminIds.Split(',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (adminIds.Contains(userId))
                return (true, null);
        }

        // Check Poracle config admins list
        try
        {
            var config = await _poracleApiProxy.GetConfigAsync();
            if (config?.Admins != null &&
                (config.Admins.Discord.Contains(userId) || config.Admins.Telegram.Contains(userId)))
                return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch Poracle config for admin check for {UserId}.", userId);
        }

        // Call getAdministrationRoles once — resolves delegation including Discord guild roles
        var managed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var isAdmin = false;

        try
        {
            var rolesJson = await _poracleApiProxy.GetAdminRolesAsync(userId);
            if (!string.IsNullOrEmpty(rolesJson))
            {
                using var doc = JsonDocument.Parse(rolesJson);
                var root = doc.RootElement;

                // Some versions return isAdmin at root; others wrap under admin.discord
                if (root.TryGetProperty("isAdmin", out var isAdminProp) && isAdminProp.ValueKind == JsonValueKind.True)
                    isAdmin = true;

                // Parse admin.discord.webhooks — the authoritative delegate webhook list
                if (root.TryGetProperty("admin", out var adminEl) &&
                    adminEl.TryGetProperty("discord", out var discordEl))
                {
                    if (!isAdmin &&
                        discordEl.TryGetProperty("isAdmin", out var discordAdmin) &&
                        discordAdmin.ValueKind == JsonValueKind.True)
                        isAdmin = true;

                    if (discordEl.TryGetProperty("webhooks", out var webhooks) &&
                        webhooks.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var wh in webhooks.EnumerateArray())
                            if (wh.GetString() is { } id)
                                managed.Add(id);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch administration roles for {UserId}.", userId);
        }

        if (isAdmin) return (true, null);

        // Also merge our own pweb_settings delegation layer
        try
        {
            var allSettings = await _pwebSettingService.GetAllAsync();
            const string prefix = "webhook_delegates:";
            foreach (var setting in allSettings)
            {
                if (setting.Setting?.StartsWith(prefix) == true)
                {
                    var delegates = setting.Value?.Split(',',
                        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
                    if (delegates.Contains(userId))
                        managed.Add(setting.Setting[prefix.Length..]);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch pweb_settings delegates for {UserId}.", userId);
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
            var allSettings = await _pwebSettingService.GetAllAsync();
            var settingsMap = allSettings.ToDictionary(s => s.Setting ?? "", s => s.Value ?? "");

            // Check if role-based access is enabled
            if (!settingsMap.TryGetValue("enable_roles", out var enableRoles) ||
                !string.Equals(enableRoles, "True", StringComparison.OrdinalIgnoreCase))
                return null; // Not enabled, allow access

            // Get allowed role IDs
            if (!settingsMap.TryGetValue("allowed_role_ids", out var roleIdsStr) ||
                string.IsNullOrWhiteSpace(roleIdsStr))
                return null; // No roles configured, allow everyone

            var allowedRoles = new HashSet<string>(
                roleIdsStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

            // Need bot token and guild ID to check roles
            if (string.IsNullOrEmpty(_discordSettings.BotToken) || string.IsNullOrEmpty(_discordSettings.GuildId))
            {
                _logger.LogWarning("Role-based access enabled but Discord BotToken or GuildId not configured.");
                return null; // Misconfigured, fail open
            }

            // Fetch user's guild member data via bot API
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bot", _discordSettings.BotToken);

            var response = await httpClient.GetAsync(
                $"https://discordapp.com/api/v10/guilds/{_discordSettings.GuildId}/members/{discordId}");

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch guild member {UserId}: {Status}", discordId, response.StatusCode);
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
                    if (role.GetString() is { } roleId)
                        userRoles.Add(roleId);
            }

            // User must have ALL of the allowed roles
            if (allowedRoles.IsSubsetOf(userRoles))
                return null;

            _logger.LogInformation("User {UserId} denied: no matching roles.", discordId);
            return "missing_required_role";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Role check failed for {UserId}, allowing access.", discordId);
            return null; // Fail open on errors
        }
    }

    private string GetFrontendUrl()
    {
        // Use the origin saved during the login step
        var savedOrigin = Request.Cookies["oauth_origin"];
        Response.Cookies.Delete("oauth_origin");
        if (!string.IsNullOrEmpty(savedOrigin))
            return savedOrigin.TrimEnd('/');

        // Fallback: same scheme/host as the request
        return $"{Request.Scheme}://{Request.Host}";
    }

}
