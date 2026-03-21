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
public class AdminController(
    IHumanService humanService,
    IPwebSettingService pwebSettingService,
    IPoracleApiProxy poracleApiProxy,
    IPoracleServerService poracleServerService,
    IOptions<PoracleSettings> poracleSettings,
    IOptions<JwtSettings> jwtSettings,
    ILogger<AdminController> logger) : BaseApiController
{
    private readonly IHumanService _humanService = humanService;
    private readonly IPwebSettingService _pwebSettingService = pwebSettingService;
    private readonly IPoracleApiProxy _poracleApiProxy = poracleApiProxy;
    private readonly IPoracleServerService _poracleServerService = poracleServerService;
    private readonly PoracleSettings _poracleSettings = poracleSettings.Value;
    private readonly JwtSettings _jwtSettings = jwtSettings.Value;
    private readonly ILogger<AdminController> _logger = logger;

    [HttpGet("users")]
    public async Task<IActionResult> GetAllUsers()
    {
        if (!this.IsAdmin)
        {
            return this.Forbid();
        }

        var humans = await this._humanService.GetAllAsync();

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

        return this.Ok(userList);
    }

    [HttpGet("users/{id}")]
    public async Task<IActionResult> GetUser(string id)
    {
        if (!this.IsAdmin)
        {
            return this.Forbid();
        }

        var human = await this._humanService.GetByIdAsync(id);
        if (human is null)
        {
            return this.NotFound();
        }

        var avatarUrl = Services.AvatarCacheService.GetAvatar(id)
            ?? GetDefaultAvatarUrl(id, human.Type);

        return this.Ok(new
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
        if (!this.IsAdmin)
        {
            return this.Forbid();
        }

        var human = await this._humanService.GetByIdAsync(id);
        if (human is null)
        {
            return this.NotFound();
        }

        human.AdminDisable = 0;
        var updated = await this._humanService.UpdateAsync(human);
        return this.Ok(updated);
    }

    [HttpPut("users/{id}/disable")]
    public async Task<IActionResult> DisableUser(string id)
    {
        if (!this.IsAdmin)
        {
            return this.Forbid();
        }

        var human = await this._humanService.GetByIdAsync(id);
        if (human is null)
        {
            return this.NotFound();
        }

        human.AdminDisable = 1;
        var updated = await this._humanService.UpdateAsync(human);
        return this.Ok(updated);
    }

    [HttpPut("users/{id}/pause")]
    public async Task<IActionResult> PauseUser(string id)
    {
        if (!this.IsAdmin)
        {
            return this.Forbid();
        }

        var human = await this._humanService.GetByIdAsync(id);
        if (human is null)
        {
            return this.NotFound();
        }

        human.Enabled = 0;
        var updated = await this._humanService.UpdateAsync(human);
        return this.Ok(updated);
    }

    [HttpPut("users/{id}/resume")]
    public async Task<IActionResult> ResumeUser(string id)
    {
        if (!this.IsAdmin)
        {
            return this.Forbid();
        }

        var human = await this._humanService.GetByIdAsync(id);
        if (human is null)
        {
            return this.NotFound();
        }

        human.Enabled = 1;
        var updated = await this._humanService.UpdateAsync(human);
        return this.Ok(updated);
    }

    [HttpDelete("users/{id}/alarms")]
    public async Task<IActionResult> DeleteUserAlarms(string id)
    {
        if (!this.IsAdmin)
        {
            return this.Forbid();
        }

        var exists = await this._humanService.ExistsAsync(id);
        if (!exists)
        {
            return this.NotFound();
        }

        var count = await this._humanService.DeleteAllAlarmsByUserAsync(id);
        return this.Ok(new
        {
            deleted = count
        });
    }

    [HttpPost("webhooks")]
    public async Task<IActionResult> CreateWebhook([FromBody] CreateWebhookRequest request)
    {
        if (!this.IsAdmin)
        {
            return this.Forbid();
        }

        if (string.IsNullOrWhiteSpace(request.Url) || string.IsNullOrWhiteSpace(request.Name))
        {
            return this.BadRequest(new
            {
                error = "Name and URL are required."
            });
        }

        var exists = await this._humanService.ExistsAsync(request.Url);
        if (exists)
        {
            return this.Conflict(new
            {
                error = "A webhook with this URL already exists."
            });
        }

        var human = new Human
        {
            Id = request.Url,
            Name = request.Name,
            Type = "webhook",
            Enabled = 1,
            AdminDisable = 0,
        };

        var created = await this._humanService.CreateAsync(human);
        this._logger.LogInformation("Admin {AdminId} created webhook {WebhookId}", this.UserId, request.Url);
        return this.Ok(created);
    }

    public record CreateWebhookRequest(string Name, string Url);

    [HttpGet("poracle-admins")]
    public async Task<IActionResult> GetPoracleAdmins()
    {
        if (!this.IsAdmin)
        {
            return this.Forbid();
        }

        var admins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrEmpty(this._poracleSettings.AdminIds))
        {
            foreach (var id in this._poracleSettings.AdminIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                admins.Add(id);
            }
        }

        try
        {
            var config = await this._poracleApiProxy.GetConfigAsync();
            if (config?.Admins?.Discord != null)
            {
                foreach (var id in config.Admins.Discord)
                {
                    admins.Add(id);
                }
            }
        }
        catch (Exception ex)
        {
            this._logger.LogWarning(ex, "Failed to fetch Poracle config for admin list.");
        }

        return this.Ok(admins);
    }

    [HttpGet("poracle-delegates")]
    public IActionResult GetPorocleDelegates()
    {
        if (!this.IsAdmin)
        {
            return this.Forbid();
        }

        var result = this.ReadPorocleDelegatesFromFile();
        return this.Ok(result);
    }

    private Dictionary<string, string[]> ReadPorocleDelegatesFromFile()
    {
        try
        {
            var sourceDir = Environment.GetEnvironmentVariable("DTS_SOURCE_DIR");
            if (string.IsNullOrEmpty(sourceDir))
            {
                return [];
            }

            var candidates = new[]
            {
                Path.Combine(sourceDir, "local.json"),
                Path.Combine(sourceDir, "config", "local.json"),
            };

            var localJsonPath = candidates.FirstOrDefault(System.IO.File.Exists);
            if (localJsonPath == null)
            {
                return [];
            }

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
            {
                return [];
            }

            var result = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in delegateAdmin.EnumerateArray())
            {
                var webhookId =
                    (entry.TryGetProperty("webhookId", out var wh) ? wh.GetString() : null) ??
                    (entry.TryGetProperty("id", out var id) ? id.GetString() : null);

                if (string.IsNullOrEmpty(webhookId))
                {
                    continue;
                }

                var users = new List<string>();
                var usersEl =
                    entry.TryGetProperty("discordIds", out var dIds) ? dIds :
                    entry.TryGetProperty("admins", out var adm) ? adm :
                    default;

                if (usersEl.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var u in usersEl.EnumerateArray())
                    {
                        if (u.GetString() is { } uid)
                        {
                            users.Add(uid);
                        }
                    }
                }

                result[webhookId] = [.. users];
            }

            this._logger.LogInformation("Loaded {Count} delegateAdministration entries from {Path}",
                result.Count, localJsonPath);
            return result;
        }
        catch (Exception ex)
        {
            this._logger.LogWarning(ex, "Failed to read delegateAdministration from local.json.");
            return [];
        }
    }

    [HttpGet("webhook-delegates/all")]
    public async Task<IActionResult> GetAllWebhookDelegates()
    {
        if (!this.IsAdmin)
        {
            return this.Forbid();
        }

        const string prefix = "webhook_delegates:";
        var allSettings = await this._pwebSettingService.GetAllAsync();
        var result = allSettings
            .Where(s => s.Setting?.StartsWith(prefix) == true)
            .ToDictionary(
                s => s.Setting![prefix.Length..],
                s => s.Value?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? []);
        return this.Ok(result);
    }

    [HttpGet("webhook-delegates")]
    public async Task<IActionResult> GetWebhookDelegates([FromQuery] string webhookId)
    {
        if (!this.IsAdmin)
        {
            return this.Forbid();
        }

        var setting = await this._pwebSettingService.GetByKeyAsync($"webhook_delegates:{webhookId}");
        var delegates = setting?.Value?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
        return this.Ok(delegates);
    }

    [HttpPost("webhook-delegates")]
    public async Task<IActionResult> AddWebhookDelegate([FromBody] WebhookDelegateRequest request)
    {
        if (!this.IsAdmin)
        {
            return this.Forbid();
        }

        var key = $"webhook_delegates:{request.WebhookId}";
        var setting = await this._pwebSettingService.GetByKeyAsync(key);
        var delegates = (setting?.Value?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? []).ToList();

        if (!delegates.Contains(request.UserId))
        {
            delegates.Add(request.UserId);
            await this._pwebSettingService.CreateOrUpdateAsync(new PwebSetting { Setting = key, Value = string.Join(',', delegates) });
        }

        return this.Ok(delegates.ToArray());
    }

    [HttpDelete("webhook-delegates")]
    public async Task<IActionResult> RemoveWebhookDelegate([FromBody] WebhookDelegateRequest request)
    {
        if (!this.IsAdmin)
        {
            return this.Forbid();
        }

        var key = $"webhook_delegates:{request.WebhookId}";
        var setting = await this._pwebSettingService.GetByKeyAsync(key);
        var delegates = (setting?.Value?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? []).ToList();

        delegates.Remove(request.UserId);

        if (delegates.Count == 0)
        {
            await this._pwebSettingService.DeleteAsync(key);
        }
        else
        {
            await this._pwebSettingService.CreateOrUpdateAsync(new PwebSetting { Setting = key, Value = string.Join(',', delegates) });
        }

        return this.Ok(delegates.ToArray());
    }

    public record WebhookDelegateRequest(string WebhookId, string UserId);

    [HttpPost("impersonate")]
    public async Task<IActionResult> ImpersonateById([FromBody] ImpersonateRequest request)
    {
        // Allow admins or delegates who manage this specific webhook
        var isDelegate = this.ManagedWebhooks.Contains(request.UserId);
        if (!this.IsAdmin && !isDelegate)
        {
            return this.Forbid();
        }

        var human = await this._humanService.GetByIdAsync(request.UserId);
        if (human is null)
        {
            return this.NotFound();
        }

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
            new("impersonatedBy", this.UserId),
        };

        if (!string.IsNullOrEmpty(avatarUrl))
        {
            claims.Add(new Claim("avatarUrl", avatarUrl));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(this._jwtSettings.Secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: this._jwtSettings.Issuer,
            audience: this._jwtSettings.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(this._jwtSettings.ExpirationMinutes),
            signingCredentials: credentials);

        var jwt = new JwtSecurityTokenHandler().WriteToken(token);
        this._logger.LogInformation("Admin {AdminId} impersonating {UserId}", this.UserId, request.UserId);
        return this.Ok(new
        {
            token = jwt
        });
    }

    public record ImpersonateRequest(string UserId);

    [HttpDelete("users/{id}")]
    public async Task<IActionResult> DeleteUser(string id)
    {
        if (!this.IsAdmin)
        {
            return this.Forbid();
        }

        var deleted = await this._humanService.DeleteUserAsync(id);
        if (!deleted)
        {
            return this.NotFound();
        }

        this._logger.LogInformation("Admin {AdminId} deleted user {UserId}", this.UserId, id);
        return this.NoContent();
    }

    [HttpPost("users/{id}/impersonate")]
    public async Task<IActionResult> ImpersonateUser(string id)
    {
        if (!this.IsAdmin)
        {
            return this.Forbid();
        }

        var human = await this._humanService.GetByIdAsync(id);
        if (human is null)
        {
            return this.NotFound();
        }

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
            new("impersonatedBy", this.UserId),
        };

        if (!string.IsNullOrEmpty(avatarUrl))
        {
            claims.Add(new Claim("avatarUrl", avatarUrl));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(this._jwtSettings.Secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: this._jwtSettings.Issuer,
            audience: this._jwtSettings.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(this._jwtSettings.ExpirationMinutes),
            signingCredentials: credentials);

        var jwt = new JwtSecurityTokenHandler().WriteToken(token);

        this._logger.LogInformation("Admin {AdminId} impersonating user {UserId}", this.UserId, id);

        return this.Ok(new
        {
            token = jwt
        });
    }

    [HttpGet("poracle/servers")]
    public async Task<IActionResult> GetPoracleServers()
    {
        if (!this.IsAdmin)
        {
            return this.Forbid();
        }

        var servers = await this._poracleServerService.GetServersAsync();
        return this.Ok(servers);
    }

    [HttpPost("poracle/servers/{host}/restart")]
    public async Task<IActionResult> RestartPoracleServer(string host)
    {
        if (!this.IsAdmin)
        {
            return this.Forbid();
        }

        try
        {
            this._logger.LogInformation("Admin {AdminId} restarting Poracle server {Host}", this.UserId, host);
            var status = await this._poracleServerService.RestartServerAsync(host);
            return this.Ok(status);
        }
        catch (InvalidOperationException ex)
        {
            return this.NotFound(new
            {
                error = ex.Message
            });
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Failed to restart Poracle server {Host}", host);
            return this.StatusCode(500, new
            {
                error = "Failed to restart server"
            });
        }
    }

    [HttpPost("poracle/servers/restart-all")]
    public async Task<IActionResult> RestartAllPoracleServers()
    {
        if (!this.IsAdmin)
        {
            return this.Forbid();
        }

        this._logger.LogInformation("Admin {AdminId} restarting all Poracle servers", this.UserId);
        var statuses = await this._poracleServerService.RestartAllAsync();
        return this.Ok(statuses);
    }

    private static string GetDefaultAvatarUrl(string userId, string? type)
    {
        if (type?.StartsWith("discord") != true)
        {
            return "https://cdn.discordapp.com/embed/avatars/0.png";
        }

        // New Discord username system: (userId >> 22) % 6
        if (long.TryParse(userId, out var id))
        {
            return $"https://cdn.discordapp.com/embed/avatars/{(id >> 22) % 6}.png";
        }

        return "https://cdn.discordapp.com/embed/avatars/0.png";
    }

}
