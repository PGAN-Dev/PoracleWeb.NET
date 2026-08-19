using Microsoft.Extensions.Caching.Memory;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Pgan.PoracleWebNet.Api.Configuration;
using Pgan.PoracleWebNet.Api.Services;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Api.Controllers;

[Route("api/admin")]
public partial class AdminController(
    IHumanService humanService,
    IMemoryCache cache,
    IUserPurgeService userPurgeService,
    IWebhookDelegateService webhookDelegateService,
    IPoracleApiProxy poracleApiProxy,
    IPoracleServerProfileService serverProfileService,
    IUpdateCheckService updateCheckService,
    IConfiguration configuration,
    IPoracleHumanProxy humanProxy,
    IOptions<PoracleSettings> poracleSettings,
    IJwtService jwtService,
    IUserRoleResolver roleResolver,
    ILogger<AdminController> logger) : BaseApiController
{
    private readonly IHumanService _humanService = humanService;
    private readonly IMemoryCache _cache = cache;
    private readonly IUserPurgeService _userPurgeService = userPurgeService;
    private readonly IWebhookDelegateService _webhookDelegateService = webhookDelegateService;
    private readonly IPoracleServerProfileService _serverProfileService = serverProfileService;
    private readonly IUpdateCheckService _updateCheckService = updateCheckService;
    private readonly IConfiguration _configuration = configuration;
    private readonly IPoracleApiProxy _poracleApiProxy = poracleApiProxy;
    private readonly IPoracleHumanProxy _humanProxy = humanProxy;
    private readonly PoracleSettings _poracleSettings = poracleSettings.Value;
    private readonly IJwtService _jwtService = jwtService;
    private readonly IUserRoleResolver _roleResolver = roleResolver;
    private readonly ILogger<AdminController> _logger = logger;

    /// <summary>Caps one avatar batch. The admin user list is the only caller and batches per viewport.</summary>
    private const int MaxAvatarBatchSize = 200;

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
            h.Notes,
            AvatarUrl = Services.AvatarCacheService.GetAvatarOrDefault(h.Id, h.Type)
        });

        return this.Ok(userList);
    }

    /// <summary>
    /// The webhooks a delegate manages.
    /// </summary>
    /// <remarks>
    /// /my-webhooks renders only when the session carries managedWebhooks, which happens only for
    /// NON-admins -- and it loaded its rows from the admin user list, which rejects exactly those people.
    /// The only users who could see the page were the only users the endpoint refused: a 403, an empty
    /// table and a failure toast. Scoped to the caller's own grants instead. See #564.
    /// </remarks>
    [HttpGet("my-webhooks")]
    public async Task<IActionResult> GetManagedWebhooks()
    {
        // Resolved live rather than read from the JWT claim. The claim is minted at login and lives 24
        // hours, so revoking a delegate left them managing the webhook until they happened to sign in
        // again -- and impersonation authorises off the same claim. See #601.
        // The union the JWT claim is built from, not the local table alone. Resolving from
        // poracle_web.webhook_delegates only meant a delegate configured in PoracleJS -- the
        // delegateAdministration mechanism -- saw the nav item, got an empty page here, and a 403 from
        // impersonate. See #626.
        var managed = (await this._roleResolver.ResolveAsync(this.UserId)).ManagedWebhooks ?? [];
        if (managed.Length == 0)
        {
            return this.Ok(Array.Empty<object>());
        }

        var humans = await this._humanService.GetAllAsync();

        var webhooks = humans
            .Where(h => managed.Contains(h.Id, StringComparer.Ordinal))
            .Select(h => new
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
                h.Notes,
                AvatarUrl = Services.AvatarCacheService.GetAvatarOrDefault(h.Id, h.Type),
            });

        return this.Ok(webhooks);
    }

    /// <summary>
    /// Resolves avatar URLs for a batch of user IDs. <see cref="Services.AvatarCacheService"/> already holds
    /// them (the background cache populates it and <c>GET users</c> reads the same source), so this is a
    /// lookup rather than a fetch -- it never calls Discord.
    /// </summary>
    [HttpPost("users/avatars")]
    public IActionResult GetUserAvatars([FromBody] string[] userIds)
    {
        if (!this.IsAdmin)
        {
            return this.Forbid();
        }

        if (userIds is null || userIds.Length == 0)
        {
            return this.Ok(new Dictionary<string, string>());
        }

        // Bounded so a caller cannot ask for an unlimited batch in one request.
        var avatars = userIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .Take(MaxAvatarBatchSize)
            .ToDictionary(id => id, id => Services.AvatarCacheService.GetAvatarOrDefault(id), StringComparer.Ordinal);

        return this.Ok(avatars);
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
            human.Notes,
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

        // Evict the block cache so this takes effect on the next request rather than up to a
        // minute later. Without it the filter kept serving the value it had already cached, which
        // is how the first live check of this fix appeared to fail. See #609.
        this._cache.Remove($"blocked:{id}");

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

        // Now that a block is actually enforced (#609), an admin blocking their own account loses the
        // API immediately -- including the endpoint that would unblock it. The list shows every account,
        // their own included, one row among many. See #613.
        if (string.Equals(id, this.UserId, StringComparison.Ordinal))
        {
            return this.BadRequest(new
            {
                error = "You cannot block your own account.",
            });
        }

        var human = await this._humanService.GetByIdAsync(id);
        if (human is null)
        {
            return this.NotFound();
        }

        await this._humanProxy.AdminDisabledAsync(id, true);

        // Evict the block cache so this takes effect on the next request rather than up to a
        // minute later. Without it the filter kept serving the value it had already cached, which
        // is how the first live check of this fix appeared to fail. See #609.
        this._cache.Remove($"blocked:{id}");

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

        try
        {
            var created = await this._humanService.CreateAsync(human);
            LogWebhookCreated(this._logger, this.UserId, request.Url);
            return this.Ok(created);
        }
        catch (HttpRequestException)
        {
            // PoracleNG commits the human and can still fail on the rest of its create, leaving a row the
            // admin was told was never written -- and a retry that answers 409 for a webhook the UI does
            // not show. Undo the half-write so the reported failure is the truth. See #482.
            if (await this._humanService.ExistsAsync(request.Url))
            {
                await this._humanService.DeleteUserAsync(request.Url);
            }

            return this.StatusCode(StatusCodes.Status502BadGateway, new
            {
                error = "Poracle rejected the webhook. Nothing was created.",
            });
        }
    }

    public record CreateWebhookRequest(string Name, string Url);

    /// <summary>
    /// Which PoracleNG this instance is talking to, what it can store, and whether that is new enough.
    /// </summary>
    /// <remarks>
    /// Admin-only because it describes the deployment rather than the account. Refreshes on request:
    /// the point of looking is usually that something just changed.
    /// </remarks>
    [HttpGet("server-profile")]
    public async Task<IActionResult> GetServerProfile([FromQuery] bool refresh = false)
    {
        if (!this.IsAdmin)
        {
            return this.Forbid();
        }

        if (refresh)
        {
            this._serverProfileService.Invalidate();
            this._updateCheckService.Invalidate();
        }

        var profile = await this._serverProfileService.GetAsync();

        // The build args are absent on a locally built image, which is not the same as being behind.
        var runningWeb = this._configuration["BUILD_VERSION"];
        var (webUpdate, ngUpdate) = await this._updateCheckService.CheckAsync(runningWeb, profile.Version);

        return this.Ok(new
        {
            version = profile.Version,
            schemaVersion = profile.SchemaVersion,
            capabilities = profile.Capabilities,
            reachable = profile.Reachable,
            checkedAt = profile.CheckedAt,
            minimumSupported = PoracleServerProfile.MinimumSupported.ToString(),
            belowMinimum = profile.IsBelowMinimum,
            poracleUpdate = new { running = ngUpdate.Running, latest = ngUpdate.Latest, state = ngUpdate.State.ToString() },
            webUpdate = new { running = webUpdate.Running, latest = webUpdate.Latest, state = webUpdate.State.ToString() },
        });
    }

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

        if (string.IsNullOrWhiteSpace(request.WebhookId) || request.WebhookId.Length > 500)
        {
            return this.BadRequest(new { error = "webhookId is required and must be 500 characters or fewer." });
        }

        // userId had neither check, though its column is half the width: over 100 characters surfaced as an
        // unhandled DbUpdateException, and an empty string persisted a delegate granting nothing to nobody
        // that then appeared in the admin view. Same shape as the guard above. See #483.
        if (string.IsNullOrWhiteSpace(request.UserId) || request.UserId.Length > 100)
        {
            return this.BadRequest(new { error = "userId is required and must be 100 characters or fewer." });
        }

        // Neither id was checked against anything, so a typo created a grant over a webhook that does not
        // exist, for a user who does not exist, and the admin delegates view then listed it as real. A
        // grant is only meaningful between two accounts that exist. See #514.
        var webhook = await this._humanService.GetByIdAsync(request.WebhookId);
        if (webhook is null || !string.Equals(webhook.Type, "webhook", StringComparison.OrdinalIgnoreCase))
        {
            return this.BadRequest(new { error = "webhookId does not name an existing webhook." });
        }

        if (!await this._humanService.ExistsAsync(request.UserId))
        {
            return this.BadRequest(new { error = "userId does not name an existing user." });
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
        // Allow admins or delegates who manage this specific webhook. Resolved live rather than read from
        // the JWT claim: the claim is minted at login and lives 24 hours, so a revoked delegate could keep
        // impersonating the webhook until they next signed in. See #601.
        // Same union as the claim and as my-webhooks, so a PoracleJS-configured delegate is not refused
        // by an endpoint the nav item just offered them. See #626.
        var isDelegate = !this.IsAdmin
            && ((await this._roleResolver.ResolveAsync(this.UserId)).ManagedWebhooks ?? [])
                .Contains(request.UserId, StringComparer.Ordinal);
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

        var userInfo = new UserInfo
        {
            Id = human.Id,
            Username = human.Name ?? human.Id,
            Type = human.Type ?? "discord:user",
            IsAdmin = false,
            Enabled = human.Enabled == 1 && human.AdminDisable == 0,
            ProfileNo = human.CurrentProfileNo,
            AvatarUrl = avatarUrl,
        };

        var jwt = this._jwtService.GenerateImpersonationToken(userInfo, this.UserId);
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

        // Everything the account owns goes with it: alarms, geofences, delegate grants, quick picks and
        // their applied state. Removing the humans row alone left all of it behind, unreachable but
        // intact, and re-creating the same id adopted the lot -- including impersonation rights over a
        // recreated webhook URL. See #510, #511, #512.
        var deleted = await this._userPurgeService.PurgeAsync(id);
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

        var userInfo = new UserInfo
        {
            Id = human.Id,
            Username = human.Name ?? human.Id,
            Type = human.Type ?? "discord:user",
            IsAdmin = false,
            Enabled = human.Enabled == 1 && human.AdminDisable == 0,
            ProfileNo = human.CurrentProfileNo,
            AvatarUrl = avatarUrl,
        };

        var jwt = this._jwtService.GenerateImpersonationToken(userInfo, this.UserId);

        LogAdminImpersonatingUser(this._logger, this.UserId, id);

        return this.Ok(new
        {
            token = jwt
        });
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
}
