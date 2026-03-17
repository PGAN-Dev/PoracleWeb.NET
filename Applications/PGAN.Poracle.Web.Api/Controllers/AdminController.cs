using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
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
    private readonly IPwebSettingService _pwebSettingService;
    private readonly IPoracleApiProxy _poracleApiProxy;
    private readonly PoracleSettings _poracleSettings;
    private readonly JwtSettings _jwtSettings;
    private readonly ILogger<AdminController> _logger;

    public AdminController(
        IHumanService humanService,
        IPwebSettingService pwebSettingService,
        IPoracleApiProxy poracleApiProxy,
        IOptions<PoracleSettings> poracleSettings,
        IOptions<JwtSettings> jwtSettings,
        ILogger<AdminController> logger)
    {
        _humanService = humanService;
        _pwebSettingService = pwebSettingService;
        _poracleApiProxy = poracleApiProxy;
        _poracleSettings = poracleSettings.Value;
        _jwtSettings = jwtSettings.Value;
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
            h.AdminDisable,
            h.LastChecked,
            h.DisabledDate,
            h.CurrentProfileNo,
            h.Language,
            AvatarUrl = Services.AvatarCacheService.GetAvatar(h.Id)
                ?? GetDefaultAvatarUrl(h.Id, h.Type)
        });

        return Ok(userList);
    }

    [HttpGet("users/{id}")]
    public async Task<IActionResult> GetUser(string id)
    {
        if (!IsAdmin)
            return Forbid();

        var human = await _humanService.GetByIdAsync(id);
        if (human is null)
            return NotFound();

        var avatarUrl = Services.AvatarCacheService.GetAvatar(id)
            ?? GetDefaultAvatarUrl(id, human.Type);

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
        human.AdminDisable = 0;
        var updated = await _humanService.UpdateAsync(human);
        return Ok(updated);
    }

    [HttpPut("users/{id}/disable")]
    public async Task<IActionResult> DisableUser(string id)
    {
        if (!IsAdmin) return Forbid();
        var human = await _humanService.GetByIdAsync(id);
        if (human is null) return NotFound();
        human.AdminDisable = 1;
        var updated = await _humanService.UpdateAsync(human);
        return Ok(updated);
    }

    [HttpPut("users/{id}/pause")]
    public async Task<IActionResult> PauseUser(string id)
    {
        if (!IsAdmin) return Forbid();
        var human = await _humanService.GetByIdAsync(id);
        if (human is null) return NotFound();
        human.Enabled = 0;
        var updated = await _humanService.UpdateAsync(human);
        return Ok(updated);
    }

    [HttpPut("users/{id}/resume")]
    public async Task<IActionResult> ResumeUser(string id)
    {
        if (!IsAdmin) return Forbid();
        var human = await _humanService.GetByIdAsync(id);
        if (human is null) return NotFound();
        human.Enabled = 1;
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

    [HttpPost("webhooks")]
    public async Task<IActionResult> CreateWebhook([FromBody] CreateWebhookRequest request)
    {
        if (!IsAdmin) return Forbid();

        if (string.IsNullOrWhiteSpace(request.Url) || string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { error = "Name and URL are required." });

        var exists = await _humanService.ExistsAsync(request.Url);
        if (exists)
            return Conflict(new { error = "A webhook with this URL already exists." });

        var human = new PGAN.Poracle.Web.Core.Models.Human
        {
            Id = request.Url,
            Name = request.Name,
            Type = "webhook",
            Enabled = 1,
            AdminDisable = 0,
        };

        var created = await _humanService.CreateAsync(human);
        _logger.LogInformation("Admin {AdminId} created webhook {WebhookId}", UserId, request.Url);
        return Ok(created);
    }

    public record CreateWebhookRequest(string Name, string Url);

    [HttpGet("poracle-admins")]
    public async Task<IActionResult> GetPoracleAdmins()
    {
        if (!IsAdmin) return Forbid();

        var admins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrEmpty(_poracleSettings.AdminIds))
        {
            foreach (var id in _poracleSettings.AdminIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                admins.Add(id);
        }

        try
        {
            var config = await _poracleApiProxy.GetConfigAsync();
            if (config?.Admins?.Discord != null)
                foreach (var id in config.Admins.Discord)
                    admins.Add(id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch Poracle config for admin list.");
        }

        return Ok(admins);
    }

    [HttpGet("poracle-delegates")]
    public IActionResult GetPorocleDelegates()
    {
        if (!IsAdmin) return Forbid();

        var result = ReadPorocleDelegatesFromFile();
        return Ok(result);
    }

    private Dictionary<string, string[]> ReadPorocleDelegatesFromFile()
    {
        try
        {
            var sourceDir = Environment.GetEnvironmentVariable("DTS_SOURCE_DIR");
            if (string.IsNullOrEmpty(sourceDir)) return [];

            var candidates = new[]
            {
                System.IO.Path.Combine(sourceDir, "local.json"),
                System.IO.Path.Combine(sourceDir, "config", "local.json"),
            };

            var localJsonPath = candidates.FirstOrDefault(System.IO.File.Exists);
            if (localJsonPath == null) return [];

            var jsonOptions = new System.Text.Json.JsonSerializerOptions
            {
                ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
                PropertyNameCaseInsensitive = true,
            };

            var json = System.IO.File.ReadAllText(localJsonPath);
            using var doc = System.Text.Json.JsonDocument.Parse(json, new System.Text.Json.JsonDocumentOptions
            {
                CommentHandling = System.Text.Json.JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });

            if (!doc.RootElement.TryGetProperty("delegateAdministration", out var delegateAdmin) ||
                delegateAdmin.ValueKind != System.Text.Json.JsonValueKind.Array)
                return [];

            var result = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in delegateAdmin.EnumerateArray())
            {
                var webhookId =
                    (entry.TryGetProperty("webhookId", out var wh) ? wh.GetString() : null) ??
                    (entry.TryGetProperty("id", out var id) ? id.GetString() : null);

                if (string.IsNullOrEmpty(webhookId)) continue;

                var users = new List<string>();
                var usersEl =
                    entry.TryGetProperty("discordIds", out var dIds) ? dIds :
                    entry.TryGetProperty("admins", out var adm) ? adm :
                    default;

                if (usersEl.ValueKind == System.Text.Json.JsonValueKind.Array)
                    foreach (var u in usersEl.EnumerateArray())
                        if (u.GetString() is { } uid)
                            users.Add(uid);

                result[webhookId] = users.ToArray();
            }

            _logger.LogInformation("Loaded {Count} delegateAdministration entries from {Path}",
                result.Count, localJsonPath);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read delegateAdministration from local.json.");
            return [];
        }
    }

    [HttpGet("webhook-delegates/all")]
    public async Task<IActionResult> GetAllWebhookDelegates()
    {
        if (!IsAdmin) return Forbid();

        const string prefix = "webhook_delegates:";
        var allSettings = await _pwebSettingService.GetAllAsync();
        var result = allSettings
            .Where(s => s.Setting?.StartsWith(prefix) == true)
            .ToDictionary(
                s => s.Setting![prefix.Length..],
                s => s.Value?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? []);
        return Ok(result);
    }

    [HttpGet("webhook-delegates")]
    public async Task<IActionResult> GetWebhookDelegates([FromQuery] string webhookId)
    {
        if (!IsAdmin) return Forbid();

        var setting = await _pwebSettingService.GetByKeyAsync($"webhook_delegates:{webhookId}");
        var delegates = setting?.Value?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
        return Ok(delegates);
    }

    [HttpPost("webhook-delegates")]
    public async Task<IActionResult> AddWebhookDelegate([FromBody] WebhookDelegateRequest request)
    {
        if (!IsAdmin) return Forbid();

        var key = $"webhook_delegates:{request.WebhookId}";
        var setting = await _pwebSettingService.GetByKeyAsync(key);
        var delegates = (setting?.Value?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? []).ToList();

        if (!delegates.Contains(request.UserId))
        {
            delegates.Add(request.UserId);
            await _pwebSettingService.CreateOrUpdateAsync(new PwebSetting { Setting = key, Value = string.Join(',', delegates) });
        }

        return Ok(delegates.ToArray());
    }

    [HttpDelete("webhook-delegates")]
    public async Task<IActionResult> RemoveWebhookDelegate([FromBody] WebhookDelegateRequest request)
    {
        if (!IsAdmin) return Forbid();

        var key = $"webhook_delegates:{request.WebhookId}";
        var setting = await _pwebSettingService.GetByKeyAsync(key);
        var delegates = (setting?.Value?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? []).ToList();

        delegates.Remove(request.UserId);

        if (delegates.Count == 0)
            await _pwebSettingService.DeleteAsync(key);
        else
            await _pwebSettingService.CreateOrUpdateAsync(new PwebSetting { Setting = key, Value = string.Join(',', delegates) });

        return Ok(delegates.ToArray());
    }

    public record WebhookDelegateRequest(string WebhookId, string UserId);

    [HttpPost("impersonate")]
    public async Task<IActionResult> ImpersonateById([FromBody] ImpersonateRequest request)
    {
        // Allow admins or delegates who manage this specific webhook
        var isDelegate = ManagedWebhooks.Contains(request.UserId);
        if (!IsAdmin && !isDelegate) return Forbid();

        var human = await _humanService.GetByIdAsync(request.UserId);
        if (human is null) return NotFound();

        var avatarUrl = Services.AvatarCacheService.GetAvatar(request.UserId)
            ?? GetDefaultAvatarUrl(request.UserId, human.Type);

        var claims = new List<Claim>
        {
            new("userId", human.Id),
            new("username", human.Name ?? human.Id),
            new("type", human.Type ?? "discord:user"),
            new("isAdmin", "false"),
            new("enabled", (human.Enabled == 1 && human.AdminDisable == 0).ToString().ToLowerInvariant()),
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
        _logger.LogInformation("Admin {AdminId} impersonating {UserId}", UserId, request.UserId);
        return Ok(new { token = jwt });
    }

    public record ImpersonateRequest(string UserId);

    [HttpDelete("users/{id}")]
    public async Task<IActionResult> DeleteUser(string id)
    {
        if (!IsAdmin) return Forbid();
        var deleted = await _humanService.DeleteUserAsync(id);
        if (!deleted) return NotFound();
        _logger.LogInformation("Admin {AdminId} deleted user {UserId}", UserId, id);
        return NoContent();
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
            new("enabled", (human.Enabled == 1 && human.AdminDisable == 0).ToString().ToLowerInvariant()),
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

}
