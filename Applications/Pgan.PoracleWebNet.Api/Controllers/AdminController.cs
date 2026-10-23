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
    IDestinationDirectory destinationDirectory,
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
    private readonly IDestinationDirectory _destinationDirectory = destinationDirectory;
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
            Kind = HumanKind.KindOf(h.Type),
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
    /// The destinations a delegate manages: webhooks, channels, threads, groups and topics.
    /// </summary>
    /// <remarks>
    /// Served on /my-destinations, and still on /my-webhooks so a SPA cached from before the page was
    /// renamed keeps loading until it reloads.
    /// The page renders only when the session carries managedDestinations, which happens only for
    /// NON-admins -- and it loaded its rows from the admin user list, which rejects exactly those people.
    /// The only users who could see the page were the only users the endpoint refused: a 403, an empty
    /// table and a failure toast. Scoped to the caller's own grants instead. See #564.
    /// </remarks>
    [HttpGet("my-destinations")]
    [HttpGet("my-webhooks")]
    public async Task<IActionResult> GetManagedDestinations()
    {
        // Resolved live rather than read from the JWT claim. The claim is minted at login and lives 24
        // hours, so revoking a delegate left them managing the webhook until they happened to sign in
        // again -- and impersonation authorises off the same claim. See #601.
        // The union the JWT claim is built from, not the local table alone. Resolving from
        // poracle_web.webhook_delegates only meant a delegate configured in PoracleJS -- the
        // delegateAdministration mechanism -- saw the nav item, got an empty page here, and a 403 from
        // impersonate. See #626.
        // Grants arrive canonicalised to humans.id -- PoracleNG hands back whatever key the operator wrote
        // in [[discord.webhook_admins]], and upstream that key is the webhook's NAME. Matched case-
        // insensitively for the same reason the resolver's set is: nothing guarantees an operator typed the
        // URL in the case the humans row stores it. See #797.
        var managed = (await this._roleResolver.ResolveAsync(this.UserId)).ManagedDestinations ?? [];
        if (managed.Length == 0)
        {
            return this.Ok(Array.Empty<object>());
        }

        var humans = await this._humanService.GetAllAsync();

        var webhooks = humans
            // Known kinds only: a degraded resolve hands back raw grants, and a row impersonate would refuse
            // must not be listed as one the caller can open (#1025).
            .Where(h => managed.Contains(h.Id, StringComparer.OrdinalIgnoreCase) && HumanKind.IsKnownDestination(h.Type))
            .Select(h => new
            {
                h.Id,
                h.Name,
                h.Type,
                Kind = HumanKind.KindOf(h.Type),
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
        this._destinationDirectory.Invalidate();

        // Re-fetch to return the updated state
        return this.Ok(await this.ReReadAsync(id, human));
    }

    /// <summary>
    /// The account as stored after a write that has already landed, or <paramref name="before"/> when it
    /// cannot be read back.
    /// </summary>
    /// <remarks>
    /// A failed read throws since #994 rather than answering null. These four endpoints only re-read to show
    /// the admin what was stored, so a failure there must not turn a change that took effect into a 500.
    /// </remarks>
    private async Task<Human> ReReadAsync(string id, Human before)
    {
        try
        {
            return await this._humanService.GetByIdAsync(id) ?? before;
        }
        catch (HttpRequestException)
        {
            return before;
        }
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
        this._destinationDirectory.Invalidate();

        // Re-fetch to return the updated state
        return this.Ok(await this.ReReadAsync(id, human));
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
        return this.Ok(await this.ReReadAsync(id, human));
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
        return this.Ok(await this.ReReadAsync(id, human));
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
    public Task<IActionResult> CreateWebhook([FromBody] CreateWebhookRequest request) =>
        this.CreateDestinationAsync(HumanKind.Webhook, request.Url, request.Name, null, "A webhook with this URL already exists.");

    /// <summary>
    /// Creates a webhook, Discord channel or thread, or Telegram channel, group or topic as a Poracle human.
    /// Only the six types in <see cref="HumanKind.Destinations"/> are accepted: a person is created by
    /// signing in, and an unknown type is an operator's typo. See #1025.
    /// </summary>
    [HttpPost("destinations")]
    public Task<IActionResult> CreateDestination([FromBody] CreateDestinationRequest request) =>
        this.CreateDestinationAsync(request.Type, request.Id, request.Name, request.Notes, "A destination with this id already exists.");

    private async Task<IActionResult> CreateDestinationAsync(string? type, string? id, string? name, string? notes, string conflictError)
    {
        if (!this.IsAdmin)
        {
            return this.Forbid();
        }

        // An impersonation token carries isAdmin = false today, so this is belt and braces: a create made
        // from inside a session would be filed against the account being inspected.
        if (this.IsImpersonating)
        {
            return this.Forbid();
        }

        if (!HumanKind.IsKnownDestination(type))
        {
            return this.BadRequest(new
            {
                error = $"Type must be one of: {string.Join(", ", HumanKind.Destinations.Order(StringComparer.Ordinal))}.",
            });
        }

        id = id?.Trim();
        name = name?.Trim();
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(name))
        {
            return this.BadRequest(new
            {
                error = "A name and an id are required.",
            });
        }

        if (name.Any(char.IsControl))
        {
            return this.BadRequest(new
            {
                error = "The name cannot contain control characters.",
            });
        }

        var idError = HumanKind.ValidateNewId(type!, id);
        if (idError is not null)
        {
            return this.BadRequest(new
            {
                error = idError,
            });
        }

        var human = new Human
        {
            Id = id,
            Name = name,
            Type = type!.ToLowerInvariant(),
            Enabled = 1,
            AdminDisable = 0,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
        };

        return await this.CreateHumanAsync(human, conflictError);
    }

    /// <summary>
    /// Creates a human row through PoracleNG. A create that fails is only rolled back when the row found
    /// at the id is the one this request tried to write: another request may have created the same id
    /// between the pre-check and the failure, and that row is not ours to delete (#1028).
    /// </summary>
    private async Task<IActionResult> CreateHumanAsync(Human human, string conflictError)
    {
        var exists = await this._humanService.ExistsAsync(human.Id);
        if (exists)
        {
            // Name what is already there, so an admin who meant a different channel can tell it apart from
            // one they created earlier. A failed read still answers the 409, just without the detail: any
            // failure, since a timeout surfaces as TaskCanceledException and would otherwise be a 500.
            Human? existing = null;
            try
            {
                existing = await this._humanService.GetByIdAsync(human.Id);
            }
            catch (Exception ex)
            {
                LogConflictDetailReadFailed(this._logger, ex);
            }

            return this.Conflict(new
            {
                error = conflictError,
                type = existing?.Type,
                name = existing?.Name,
            });
        }

        try
        {
            var created = await this._humanService.CreateAsync(human);

            // The directory is cached a minute, and a destination just created should be grantable now. Role
            // answers go too: a grant naming this id was dropped as matching no row, and that answer is cached.
            this._destinationDirectory.Invalidate();
            this._roleResolver.InvalidateAll();
            LogDestinationCreated(this._logger, LogSafe.Id(this.UserId), human.Type, LogSafe.Id(human.Id));
            return this.Ok(created);
        }
        catch (HttpRequestException)
        {
            // Dropped before anything else: the create may have written a row and the rollback below may
            // remove one, so whichever branch answers, the cached directory can no longer be trusted.
            this._destinationDirectory.Invalidate();

            // PoracleNG commits the human and can still fail on the rest of its create, leaving a row the
            // admin was told was never written -- and a retry that answers 409 for a webhook the UI does
            // not show. Undo the half-write so the reported failure is the truth. See #482.
            try
            {
                var found = await this._humanService.GetByIdAsync(human.Id);
                if (found is not null)
                {
                    if (string.Equals(found.Type, human.Type, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(found.Name, human.Name, StringComparison.Ordinal))
                    {
                        // PurgeAsync, not DeleteUserAsync, so any delegate grants on the row go with it.
                        await this._userPurgeService.PurgeAsync(human.Id);
                    }
                    else
                    {
                        return this.StatusCode(StatusCodes.Status502BadGateway, new
                        {
                            error = "Poracle rejected it, and a record with this id already exists. Nothing was changed.",
                        });
                    }
                }
            }
            catch (HttpRequestException)
            {
                // The read throws on a failed answer since #994, where it used to answer "no" and skip the
                // cleanup silently. Either way nothing can say whether a half-write is there, so the admin
                // is told that rather than "nothing was created".
                return this.StatusCode(StatusCodes.Status502BadGateway, new
                {
                    error = "Poracle rejected the destination, and could not be asked whether part of it was saved. Check the destination list before trying again.",
                });
            }

            return this.StatusCode(StatusCodes.Status502BadGateway, new
            {
                error = "Poracle rejected the destination. Nothing was created.",
            });
        }
    }

    public record CreateWebhookRequest(string Name, string Url);

    public record CreateDestinationRequest(string Type, string Id, string Name, string? Notes);

    /// <summary>
    /// Which PoracleNG this instance is talking to, and whether that is new enough.
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
        var webRevision = this._configuration["BUILD_REVISION"];
        var (webUpdate, ngUpdate) = await this._updateCheckService.CheckAsync(runningWeb, profile.Version);

        return this.Ok(new
        {
            version = profile.Version,
            capabilities = profile.Capabilities,
            reachable = profile.Reachable,
            checkedAt = profile.CheckedAt,
            minimumSupported = PoracleServerProfile.MinimumSupported.ToString(),
            belowMinimum = profile.IsBelowMinimum,
            poracleUpdate = new { running = ngUpdate.Running, latest = ngUpdate.Latest, state = ngUpdate.State.ToString() },
            webUpdate = new { running = webUpdate.Running, latest = webUpdate.Latest, state = webUpdate.State.ToString() },

            // This site's own build, so the card can name both halves of the deployment rather than
            // only the one it talks to.
            web = new
            {
                version = string.IsNullOrWhiteSpace(runningWeb) ? null : runningWeb,
                revision = string.IsNullOrWhiteSpace(webRevision) ? null : webRevision,
                buildDate = this._configuration["BUILD_DATE"],
            },
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
        //
        // Any destination takes delegates, not only webhooks (#1025). HumanKind.Destinations is the list that
        // grants power: a type that is merely not a person (an operator's typo) is listed on Destinations
        // but cannot be delegated.
        var webhook = await this._humanService.GetByIdAsync(request.WebhookId);
        if (webhook is null || !HumanKind.IsKnownDestination(webhook.Type))
        {
            return this.BadRequest(new { error = "webhookId does not name a destination." });
        }

        var grantee = await this._humanService.GetByIdAsync(request.UserId);
        if (grantee is null)
        {
            return this.BadRequest(new { error = "userId does not name an existing user." });
        }

        // A delegate impersonates a webhook by being signed in as themselves, so the grantee has to be a
        // person. A channel or group row can never sign in, and a grant to one would sit in the table as
        // authority nobody holds. See #1025.
        if (!HumanKind.IsPerson(grantee.Type))
        {
            return this.BadRequest(new { error = "userId does not name a person." });
        }

        var delegates = await this._webhookDelegateService.AddDelegateAsync(request.WebhookId, request.UserId);

        // The role cache would otherwise hold the old answer for up to a minute, so a new delegate saw no
        // nav item and a revoked one kept impersonating. Drop both sides of the grant. The directory goes too,
        // so a destination created outside this site within its minute is canonicalised rather than dropped.
        this._roleResolver.Invalidate(request.UserId, request.WebhookId);
        this._destinationDirectory.Invalidate();
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

        // Revocation has to land now: an impersonation session is re-authorised against this cache on
        // every request, so a stale entry is a minute of access the admin just took away.
        this._roleResolver.Invalidate(request.UserId, request.WebhookId);
        return this.Ok(delegates);
    }

    public record WebhookDelegateRequest(string WebhookId, string UserId);

    [HttpPost("impersonate")]
    public async Task<IActionResult> ImpersonateById([FromBody] ImpersonateRequest request)
    {
        // Allow admins or delegates who manage this specific webhook. Resolved live rather than read from
        // the JWT claim: the claim is minted at login and lives 24 hours, so a revoked delegate could keep
        // impersonating the webhook until they next signed in. See #601.
        // Same union as the claim and as my-destinations, so a PoracleJS-configured delegate is not refused
        // by an endpoint the nav item just offered them. See #626.
        // No hop from inside an impersonation session. The SPA stashes the caller's own token in a single
        // poracle_admin_token slot, so a second hop overwrites it with the first impersonated one and
        // strands whoever started with no way back to their own account. Nothing legitimate chained before:
        // the nav item that reaches this endpoint was hidden under impersonation, and the admin user list
        // refuses it anyway because the impersonation token carries isAdmin = false. See #797.
        if (this.IsImpersonating)
        {
            return this.Forbid();
        }

        var isDelegate = !this.IsAdmin
            && ((await this._roleResolver.ResolveAsync(this.UserId)).ManagedDestinations ?? [])
                .Contains(request.UserId, StringComparer.OrdinalIgnoreCase);
        if (!this.IsAdmin && !isDelegate)
        {
            return this.Forbid();
        }

        var human = await this._humanService.GetByIdAsync(request.UserId);
        if (human is null)
        {
            return this.NotFound();
        }

        // A delegate reaches destinations only, judged by the live row's type rather than by the grant: a
        // local grant outlives a type change, and an id in PoracleNG's lists can name a person. Without this
        // a delegate whose grant named a person signed in as them. Unknown types are refused too, since only
        // the six in HumanKind.Destinations grant anything. Admins inspect any account. See #1025.
        if (!this.IsAdmin && !HumanKind.IsKnownDestination(human.Type))
        {
            return this.Forbid();
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
        LogAdminImpersonating(this._logger, LogSafe.Id(this.UserId), LogSafe.Id(request.UserId));
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

        // The purge below cannot be undone, and an admin's own row sits in the list like any other. The
        // sibling of #613's self-block, which guarded blocking yourself and left deleting yourself open.
        if (string.Equals(id, this.UserId, StringComparison.Ordinal))
        {
            return this.BadRequest(new
            {
                error = "You cannot delete your own account.",
            });
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

        // The purge removed grants in both directions, and a delegate PoracleJS names by the webhook's NAME
        // is in no table that could say who else just lost access -- so every cached answer goes.
        this._roleResolver.InvalidateAll();
        this._destinationDirectory.Invalidate();

        LogUserDeleted(this._logger, LogSafe.Id(this.UserId), LogSafe.Id(id));
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

        LogAdminImpersonatingUser(this._logger, LogSafe.Id(this.UserId), LogSafe.Id(id));

        return this.Ok(new
        {
            token = jwt
        });
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Admin {AdminId} created {DestinationType} destination {DestinationId}")]
    private static partial void LogDestinationCreated(ILogger logger, string adminId, string? destinationType, string destinationId);

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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not read the existing row to name it in a 409; answering without the detail.")]
    private static partial void LogConflictDetailReadFailed(ILogger logger, Exception ex);
}
