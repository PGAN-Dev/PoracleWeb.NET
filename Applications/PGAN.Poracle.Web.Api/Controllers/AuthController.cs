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
    private readonly JwtSettings _jwtSettings;
    private readonly DiscordSettings _discordSettings;
    private readonly TelegramSettings _telegramSettings;
    private readonly PoracleSettings _poracleSettings;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        IHumanService humanService,
        IPoracleApiProxy poracleApiProxy,
        IOptions<JwtSettings> jwtSettings,
        IOptions<DiscordSettings> discordSettings,
        IOptions<TelegramSettings> telegramSettings,
        IOptions<PoracleSettings> poracleSettings,
        ILogger<AuthController> logger)
    {
        _humanService = humanService;
        _poracleApiProxy = poracleApiProxy;
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

        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            MaxAge = TimeSpan.FromMinutes(10)
        };

        Response.Cookies.Append("oauth_state", state, cookieOptions);

        // Save the origin so we know where to redirect after the callback
        var origin = $"{Request.Scheme}://{Request.Host}";
        Response.Cookies.Append("oauth_origin", origin, cookieOptions);

        // Redirect URI points to the API itself, not the Angular app
        var callbackUri = $"{Request.Scheme}://{Request.Host}/api/auth/discord/callback";
        var redirectUrl = "https://discordapp.com/api/oauth2/authorize" +
            $"?client_id={_discordSettings.ClientId}" +
            $"&redirect_uri={Uri.EscapeDataString(callbackUri)}" +
            "&response_type=code" +
            "&scope=identify" +
            $"&state={Uri.EscapeDataString(state)}";

        return Ok(new { url = redirectUrl });
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

        var isAdmin = await IsUserAdminAsync(discordId);

        var userInfo = new UserInfo
        {
            Id = discordId,
            Username = username,
            Type = "discord:user",
            IsAdmin = isAdmin,
            ProfileNo = human.CurrentProfileNo,
            AvatarUrl = avatarUrl
        };

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

        var isAdmin = await IsUserAdminAsync(telegramId);

        var userInfo = new UserInfo
        {
            Id = telegramId,
            Username = username!,
            Type = "telegram:user",
            IsAdmin = isAdmin,
            ProfileNo = human.CurrentProfileNo,
            AvatarUrl = photoUrl
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
    public IActionResult Me()
    {
        var userInfo = new UserInfo
        {
            Id = UserId,
            Username = Username,
            Type = User.FindFirstValue("type") ?? string.Empty,
            IsAdmin = IsAdmin,
            ProfileNo = ProfileNo,
            AvatarUrl = User.FindFirstValue("avatarUrl")
        };

        return Ok(userInfo);
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
            new("profileNo", user.ProfileNo.ToString()),
        };

        if (!string.IsNullOrEmpty(user.AvatarUrl))
            claims.Add(new Claim("avatarUrl", user.AvatarUrl));

        var token = new JwtSecurityToken(
            issuer: _jwtSettings.Issuer,
            audience: _jwtSettings.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_jwtSettings.ExpirationMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
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

    private async Task<bool> IsUserAdminAsync(string userId)
    {
        // Check configured admin IDs first
        if (!string.IsNullOrEmpty(_poracleSettings.AdminIds))
        {
            var adminIds = _poracleSettings.AdminIds.Split(',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (adminIds.Contains(userId))
                return true;
        }

        // Check Poracle API admin list from config
        try
        {
            var config = await _poracleApiProxy.GetConfigAsync();
            if (config?.Admins != null)
            {
                if (config.Admins.Discord.Contains(userId) ||
                    config.Admins.Telegram.Contains(userId))
                    return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch Poracle config for admin check.");
        }

        // Check administration roles via Poracle API
        try
        {
            var rolesJson = await _poracleApiProxy.GetAdminRolesAsync(userId);
            if (!string.IsNullOrEmpty(rolesJson))
            {
                using var doc = JsonDocument.Parse(rolesJson);
                if (doc.RootElement.TryGetProperty("isAdmin", out var isAdminProp) &&
                    isAdminProp.GetBoolean())
                    return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch admin roles for user {UserId}.", userId);
        }

        return false;
    }
}
