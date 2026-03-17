using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PGAN.Poracle.Web.Api.Configuration;
using PGAN.Poracle.Web.Core.Abstractions.Services;
using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Api.Controllers;

[Route("api/admin")]
public class AdminController : BaseApiController
{
    private readonly IHumanService _humanService;
    private readonly DiscordSettings _discordSettings;
    private readonly JwtSettings _jwtSettings;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AdminController> _logger;

    public AdminController(
        IHumanService humanService,
        IOptions<DiscordSettings> discordSettings,
        IOptions<JwtSettings> jwtSettings,
        IHttpClientFactory httpClientFactory,
        ILogger<AdminController> logger)
    {
        _humanService = humanService;
        _discordSettings = discordSettings.Value;
        _jwtSettings = jwtSettings.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    [HttpGet("users")]
    public async Task<IActionResult> GetAllUsers()
    {
        if (!IsAdmin)
            return Forbid();

        var humans = await _humanService.GetAllAsync();

        // Return users with avatars from background cache
        var userList = humans.Select(h => new
        {
            h.Id,
            h.Name,
            h.Type,
            h.Enabled,
            h.CurrentProfileNo,
            h.Language,
            AvatarUrl = Services.AvatarCacheService.GetAvatar(h.Id)
                ?? GetDefaultAvatarUrl(h.Id, h.Type)
        });

        return Ok(userList);
    }

    [HttpPost("users/avatars")]
    public async Task<IActionResult> GetUserAvatars([FromBody] List<string> userIds)
    {
        if (!IsAdmin)
            return Forbid();

        var result = new Dictionary<string, string>();
        foreach (var id in userIds)
        {
            // Try cache first, fetch on-demand for new users
            var url = Services.AvatarCacheService.GetAvatar(id)
                ?? await Services.AvatarCacheService.FetchSingleAsync(id, _discordSettings.BotToken, _httpClientFactory, _logger);
            if (url != null) result[id] = url;
        }
        return Ok(result);
    }

    [HttpGet("users/{id}")]
    public async Task<IActionResult> GetUser(string id)
    {
        if (!IsAdmin)
            return Forbid();

        var human = await _humanService.GetByIdAsync(id);
        if (human is null)
            return NotFound();

        string? avatarUrl = null;
        if (!string.IsNullOrEmpty(_discordSettings.BotToken) && human.Type?.StartsWith("discord") == true)
        {
            var avatars = await FetchDiscordAvatarsAsync([id]);
            avatars.TryGetValue(id, out avatarUrl);
        }
        avatarUrl ??= GetDefaultAvatarUrl(id, human.Type);

        return Ok(new
        {
            human.Id,
            human.Name,
            human.Type,
            human.Enabled,
            human.CurrentProfileNo,
            human.Language,
            human.Area,
            human.Latitude,
            human.Longitude,
            AvatarUrl = avatarUrl
        });
    }

    [HttpPut("users/{id}/enable")]
    public async Task<IActionResult> EnableUser(string id)
    {
        if (!IsAdmin) return Forbid();
        var human = await _humanService.GetByIdAsync(id);
        if (human is null) return NotFound();
        human.Enabled = 1;
        var updated = await _humanService.UpdateAsync(human);
        return Ok(updated);
    }

    [HttpPut("users/{id}/disable")]
    public async Task<IActionResult> DisableUser(string id)
    {
        if (!IsAdmin) return Forbid();
        var human = await _humanService.GetByIdAsync(id);
        if (human is null) return NotFound();
        human.Enabled = 0;
        var updated = await _humanService.UpdateAsync(human);
        return Ok(updated);
    }

    [HttpDelete("users/{id}/alarms")]
    public async Task<IActionResult> DeleteUserAlarms(string id)
    {
        if (!IsAdmin) return Forbid();
        var exists = await _humanService.ExistsAsync(id);
        if (!exists) return NotFound();
        var count = await _humanService.DeleteAllAlarmsByUserAsync(id);
        return Ok(new { deleted = count });
    }

    [HttpPost("users/{id}/impersonate")]
    public async Task<IActionResult> ImpersonateUser(string id)
    {
        if (!IsAdmin) return Forbid();

        var human = await _humanService.GetByIdAsync(id);
        if (human is null) return NotFound();

        var avatarUrl = Services.AvatarCacheService.GetAvatar(id)
            ?? GetDefaultAvatarUrl(id, human.Type);

        var claims = new List<Claim>
        {
            new("userId", human.Id),
            new("username", human.Name ?? human.Id),
            new("type", human.Type ?? "discord:user"),
            new("isAdmin", "false"),
            new("profileNo", human.CurrentProfileNo.ToString()),
            new("impersonatedBy", UserId),
        };

        if (!string.IsNullOrEmpty(avatarUrl))
            claims.Add(new Claim("avatarUrl", avatarUrl));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.Secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: _jwtSettings.Issuer,
            audience: _jwtSettings.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_jwtSettings.ExpirationMinutes),
            signingCredentials: credentials);

        var jwt = new JwtSecurityTokenHandler().WriteToken(token);

        _logger.LogInformation("Admin {AdminId} impersonating user {UserId}", UserId, id);

        return Ok(new { token = jwt });
    }

    private static string GetDefaultAvatarUrl(string userId, string? type)
    {
        if (type?.StartsWith("discord") != true)
            return "https://cdn.discordapp.com/embed/avatars/0.png";

        // New Discord username system: (userId >> 22) % 6
        if (long.TryParse(userId, out var id))
            return $"https://cdn.discordapp.com/embed/avatars/{(id >> 22) % 6}.png";

        return "https://cdn.discordapp.com/embed/avatars/0.png";
    }

    private async Task<Dictionary<string, string>> FetchDiscordAvatarsAsync(List<string> userIds)
    {
        var result = new Dictionary<string, string>();
        if (userIds.Count == 0) return result;

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("Authorization", $"Bot {_discordSettings.BotToken}");
            client.DefaultRequestHeaders.Add("User-Agent", "DiscordBot (https://pgan.me, 1.0)");
            client.Timeout = TimeSpan.FromSeconds(30);

            var requestCount = 0;
            foreach (var userId in userIds)
            {
                try
                {
                    // Proactive rate limiting: Discord allows ~30 requests per second on this route
                    if (requestCount > 0 && requestCount % 10 == 0)
                        await Task.Delay(500);

                    var response = await client.GetAsync($"https://discordapp.com/api/v9/users/{userId}");

                    if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        var retryAfter = 2.0;
                        if (response.Headers.TryGetValues("Retry-After", out var values))
                            double.TryParse(values.FirstOrDefault(), out retryAfter);

                        _logger.LogWarning("Discord rate limited, waiting {Seconds}s", retryAfter);
                        await Task.Delay(TimeSpan.FromSeconds(Math.Max(retryAfter, 1.0)));

                        response = await client.GetAsync($"https://discordapp.com/api/v9/users/{userId}");
                    }

                    requestCount++;

                    if (!response.IsSuccessStatusCode) continue;

                    var json = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("avatar", out var avatarProp) && avatarProp.ValueKind == JsonValueKind.String)
                    {
                        var hash = avatarProp.GetString();
                        if (!string.IsNullOrEmpty(hash))
                        {
                            var ext = hash.StartsWith("a_") ? "gif" : "png";
                            result[userId] = $"https://cdn.discordapp.com/avatars/{userId}/{hash}.{ext}";
                        }
                    }

                    await Task.Delay(80);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to fetch Discord avatar for user {UserId}", userId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch Discord avatars");
        }

        return result;
    }
}
