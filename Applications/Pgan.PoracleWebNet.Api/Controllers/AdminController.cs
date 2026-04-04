using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Pgan.PoracleWebNet.Api.Configuration;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Api.Controllers;

[Route("api/admin")]
public partial class AdminController(
    IHumanService humanService,
    IWebhookDelegateService webhookDelegateService,
    IPoracleApiProxy poracleApiProxy,
    IPoracleHumanProxy humanProxy,
    IPoracleServerService poracleServerService,
    IOptions<PoracleSettings> poracleSettings,
    IOptions<JwtSettings> jwtSettings,
    ILogger<AdminController> logger) : BaseApiController
{
    private readonly IHumanService _humanService = humanService;
    private readonly IWebhookDelegateService _webhookDelegateService = webhookDelegateService;
    private readonly IPoracleApiProxy _poracleApiProxy = poracleApiProxy;
    private readonly IPoracleHumanProxy _humanProxy = humanProxy;
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
            AvatarUrl = Services.AvatarCacheService.GetAvatarOrDefault(h.Id, h.Type)
        });

        return this.Ok(userList);
    }

    [HttpGet("users/by-id")]
    public async Task<IActionResult> GetUser([FromQuery] string id)
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

        var avatarUrl = Services.AvatarCacheService.GetAvatarOrDefault(id, human.Type);

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

    [HttpPut("users/enable")]
    public async Task<IActionResult> EnableUser([FromQuery] string id)
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

        await this._humanProxy.AdminDisabledAsync(id, false);

        // Re-fetch to return the updated state
        var updated = await this._humanService.GetByIdAsync(id) ?? human;
        return this.Ok(updated);
    }

    [HttpPut("users/disable")]
    public async Task<IActionResult> DisableUser([FromQuery] string id)
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

        await this._humanProxy.AdminDisabledAsync(id, true);

        // Re-fetch to return the updated state
        var updated = await this._humanService.GetByIdAsync(id) ?? human;
        return this.Ok(updated);
    }

    [HttpPut("users/pause")]
    public async Task<IActionResult> PauseUser([FromQuery] string id)
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

        await this._humanProxy.StopAsync(id);

        // Re-fetch to return the updated state
        var updated = await this._humanService.GetByIdAsync(id) ?? human;
        return this.Ok(updated);
    }

    [HttpPut("users/resume")]
    public async Task<IActionResult> ResumeUser([FromQuery] string id)
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

        await this._humanProxy.StartAsync(id);

        // Re-fetch to return the updated state
        var updated = await this._humanService.GetByIdAsync(id) ?? human;
        return this.Ok(updated);
    }

    [HttpDelete("users/alarms")]
    public async Task<IActionResult> DeleteUserAlarms([FromQuery] string id)
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
        LogWebhookCreated(this._logger, this.UserId, request.Url);
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
            LogPoracleConfigFetchFailed(this._logger, ex);
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

            LogDelegateEntriesLoaded(this._logger, result.Count, localJsonPath);
            return result;
        }
        catch (Exception ex)
        {
            LogDelegateReadFailed(this._logger, ex);
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

        var result = await this._webhookDelegateService.GetAllGroupedAsync();
        return this.Ok(result);
    }

    [HttpGet("webhook-delegates")]
    public async Task<IActionResult> GetWebhookDelegates([FromQuery] string webhookId)
    {
        if (!this.IsAdmin)
        {
            return this.Forbid();
        }

        var delegates = await this._webhookDelegateService.GetDelegatesForWebhookAsync(webhookId);
        return this.Ok(delegates);
    }

    [HttpPost("webhook-delegates")]
    public async Task<IActionResult> AddWebhookDelegate([FromBody] WebhookDelegateRequest request)
    {
        if (!this.IsAdmin)
        {
            return this.Forbid();
        }

        var delegates = await this._webhookDelegateService.AddDelegateAsync(request.WebhookId, request.UserId);
        return this.Ok(delegates);
    }

    [HttpDelete("webhook-delegates")]
    public async Task<IActionResult> RemoveWebhookDelegate([FromBody] WebhookDelegateRequest request)
    {
        if (!this.IsAdmin)
        {
            return this.Forbid();
        }

        var delegates = await this._webhookDelegateService.RemoveDelegateAsync(request.WebhookId, request.UserId);
        return this.Ok(delegates);
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

        var avatarUrl = Services.AvatarCacheService.GetAvatarOrDefault(request.UserId, human.Type);

        var claims = new List<Claim>
        {
            new("userId", human.Id),
            new("username", human.Name ?? human.Id),
            new("type", human.Type ?? "discord:user"),
            new("isAdmin", "false"),
            new("enabled", (human.Enabled == 1 && human.AdminDisable == 0).ToString().ToLowerInvariant()),
            new("profileNo", human.CurrentProfileNo.ToString(CultureInfo.InvariantCulture)),
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
        LogAdminImpersonating(this._logger, this.UserId, request.UserId);
        return this.Ok(new
        {
            token = jwt
        });
    }

    public record ImpersonateRequest(string UserId);

    [HttpDelete("users")]
    public async Task<IActionResult> DeleteUser([FromQuery] string id)
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

        LogUserDeleted(this._logger, this.UserId, id);
        return this.NoContent();
    }

    [HttpPost("users/impersonate")]
    public async Task<IActionResult> ImpersonateUser([FromQuery] string id)
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

        var avatarUrl = Services.AvatarCacheService.GetAvatarOrDefault(id, human.Type);

        var claims = new List<Claim>
        {
            new("userId", human.Id),
            new("username", human.Name ?? human.Id),
            new("type", human.Type ?? "discord:user"),
            new("isAdmin", "false"),
            new("enabled", (human.Enabled == 1 && human.AdminDisable == 0).ToString().ToLowerInvariant()),
            new("profileNo", human.CurrentProfileNo.ToString(CultureInfo.InvariantCulture)),
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

        LogAdminImpersonatingUser(this._logger, this.UserId, id);

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
            LogServerRestarting(this._logger, this.UserId, host);
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
            LogServerRestartFailed(this._logger, ex, host);
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

        LogAllServersRestarting(this._logger, this.UserId);
        var statuses = await this._poracleServerService.RestartAllAsync();
        return this.Ok(statuses);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Admin {AdminId} created webhook {WebhookId}")]
    private static partial void LogWebhookCreated(ILogger logger, string adminId, string webhookId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to fetch Poracle config for admin list.")]
    private static partial void LogPoracleConfigFetchFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Loaded {Count} delegateAdministration entries from {Path}")]
    private static partial void LogDelegateEntriesLoaded(ILogger logger, int count, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to read delegateAdministration from local.json.")]
    private static partial void LogDelegateReadFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Admin {AdminId} impersonating {UserId}")]
    private static partial void LogAdminImpersonating(ILogger logger, string adminId, string userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Admin {AdminId} deleted user {UserId}")]
    private static partial void LogUserDeleted(ILogger logger, string adminId, string userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Admin {AdminId} impersonating user {UserId}")]
    private static partial void LogAdminImpersonatingUser(ILogger logger, string adminId, string userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Admin {AdminId} restarting Poracle server {Host}")]
    private static partial void LogServerRestarting(ILogger logger, string adminId, string host);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to restart Poracle server {Host}")]
    private static partial void LogServerRestartFailed(ILogger logger, Exception exception, string host);

    [LoggerMessage(Level = LogLevel.Information, Message = "Admin {AdminId} restarting all Poracle servers")]
    private static partial void LogAllServersRestarting(ILogger logger, string adminId);
}
