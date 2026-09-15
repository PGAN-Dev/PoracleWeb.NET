using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Pgan.PoracleWebNet.Api.Configuration;
using Pgan.PoracleWebNet.Core.Abstractions.Services;

namespace Pgan.PoracleWebNet.Api.Services;

/// <summary>
/// A user's admin status and the webhooks they may administer, resolved from live sources.
/// </summary>
/// <param name="IsAdmin">Whether the user is an admin. Meaningless when <paramref name="Resolved"/> is false.</param>
/// <param name="ManagedWebhooks">Webhooks the user may administer, or null.</param>
/// <param name="Resolved">
/// False when a source we needed was unreachable. Callers that stamp roles onto a token must treat
/// this as "do not change the claim" rather than as "not an admin" -- a PoracleNG blip during a
/// profile switch otherwise stripped admin for the rest of the session. See #656.
/// </param>
public readonly record struct UserRoles(bool IsAdmin, string[]? ManagedWebhooks, bool Resolved = true);

/// <summary>
/// Resolves admin status and delegated webhooks from the configured admin list, Poracle's config,
/// PoracleNG's <c>getAdministrationRoles</c>, and PoracleWeb's own delegate table.
/// </summary>
public interface IUserRoleResolver
{
    /// <summary>Resolves the user's current roles.</summary>
    Task<UserRoles> ResolveAsync(string userId);
}

/// <summary>
/// The single place roles are worked out.
/// </summary>
/// <remarks>
/// This used to live as a private method on <c>AuthController</c>, which meant login was the only
/// thing that could see it. Two defects came out of that: the <c>isAdmin</c> claim was minted once and
/// then copied verbatim through every token re-issue, so revoking someone's admin rights never took
/// effect while they kept switching profile (#624); and the admin endpoints that resolve delegated
/// webhooks live went to the local table alone, so a delegate configured in PoracleJS could see the
/// My Webhooks nav item and get an empty page and a 403 (#626).
/// <para>
/// Results are cached for a minute. Both PoracleNG calls are network round-trips and the resolver now
/// sits on paths that are not login, so an uncached implementation would put two HTTP requests on
/// every profile switch. A minute is short enough that revoking rights still takes effect promptly.
/// </para>
/// </remarks>
public sealed partial class UserRoleResolver(
    IPoracleApiProxy poracleApiProxy,
    IPoracleHumanProxy poracleHumanProxy,
    IWebhookDelegateService webhookDelegateService,
    IHumanService humanService,
    IOptions<PoracleSettings> poracleSettings,
    IMemoryCache cache,
    ILogger<UserRoleResolver> logger) : IUserRoleResolver
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(1);

    private readonly IMemoryCache _cache = cache;
    private readonly ILogger<UserRoleResolver> _logger = logger;
    private readonly IPoracleApiProxy _poracleApiProxy = poracleApiProxy;
    private readonly IPoracleHumanProxy _poracleHumanProxy = poracleHumanProxy;
    private readonly PoracleSettings _poracleSettings = poracleSettings.Value;
    private readonly IWebhookDelegateService _webhookDelegateService = webhookDelegateService;
    private readonly IHumanService _humanService = humanService;

    public async Task<UserRoles> ResolveAsync(string userId)
    {
        var cacheKey = $"roles:{userId}";
        if (this._cache.TryGetValue<UserRoles>(cacheKey, out var cached))
        {
            return cached;
        }

        var resolved = await this.ResolveUncachedAsync(userId);

        // A degraded answer is never cached: doing so would hold a user at the wrong privilege level for
        // the full minute after a momentary outage. See #656.
        if (resolved.Resolved)
        {
            this._cache.Set(cacheKey, resolved, CacheTtl);
        }

        return resolved;
    }

    private async Task<UserRoles> ResolveUncachedAsync(string userId)
    {
        // Fast path: configured admin IDs
        if (!string.IsNullOrEmpty(this._poracleSettings.AdminIds))
        {
            var adminIds = this._poracleSettings.AdminIds.Split(',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (adminIds.Contains(userId))
            {
                return new UserRoles(true, null);
            }
        }

        // Tracked so a failure is reported as "unknown", not as "not an admin". See #656.
        var configReadable = true;
        var rolesReadable = true;
        var delegatesReadable = true;

        // Check Poracle config admins list
        try
        {
            var config = await this._poracleApiProxy.GetConfigAsync();
            if (config?.Admins != null &&
                (config.Admins.Discord.Contains(userId) || config.Admins.Telegram.Contains(userId)))
            {
                return new UserRoles(true, null);
            }
        }
        catch (Exception ex)
        {
            LogPoracleConfigFetchFailed(this._logger, ex, userId);
            configReadable = false;
        }

        // Ask PoracleNG once for the delegated webhooks, Discord guild roles included.
        var managed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var rolesJson = await this._poracleHumanProxy.GetAdminRolesAsync(userId);
            if (!string.IsNullOrEmpty(rolesJson))
            {
                using var doc = JsonDocument.Parse(rolesJson);
                var root = doc.RootElement;

                // admin.discord.webhooks is the authoritative delegate webhook list.
                //
                // Two isAdmin branches used to sit here, one at the root and one under admin.discord.
                // Neither has ever fired: both API versions build this body from the same
                // adminRolesResult, whose only fields are channels, webhooks and users, and v2's schema
                // is additionalProperties:false so an isAdmin could not appear even by accident.
                // Admin status is resolved above, from the configured ids and Poracle's own config.
                if (root.TryGetProperty("admin", out var adminEl) &&
                    adminEl.TryGetProperty("discord", out var discordEl) &&
                    discordEl.TryGetProperty("webhooks", out var webhooks) &&
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
        catch (Exception ex)
        {
            LogAdminRolesFetchFailed(this._logger, ex, userId);
            rolesReadable = false;
        }

        // Also merge our own webhook delegate service layer
        try
        {
            var managedWebhookIds = await this._webhookDelegateService.GetManagedWebhookIdsAsync(userId);
            foreach (var webhookId in managedWebhookIds)
            {
                managed.Add(webhookId);
            }
        }
        catch (Exception ex)
        {
            // The third source, and it was left out of the Resolved flag: a poracle_web blip returned a
            // confident answer with an incomplete webhook list, which then got cached for the full
            // minute and denied a legitimate delegate the whole time. See #667.
            LogPwebDelegatesFetchFailed(this._logger, ex, userId);
            delegatesReadable = false;
        }

        if (managed.Count == 0)
        {
            return new UserRoles(false, null, configReadable && rolesReadable && delegatesReadable);
        }

        var (canonical, humansReadable) = await this.CanonicaliseAsync(managed);

        return new UserRoles(false, canonical.Length > 0 ? canonical : null,
            configReadable && rolesReadable && delegatesReadable && humansReadable);
    }

    /// <summary>
    /// Rewrites each grant to the <c>humans.id</c> it names, dropping the ones that name nothing.
    /// </summary>
    /// <remarks>
    /// PoracleNG hands back whatever key the operator wrote in <c>[[discord.webhook_admins]] target</c>,
    /// and upstream that key is the webhook's NAME -- the bot resolves it with <c>LookupWebhookByName</c>
    /// and authorises with <c>CanAdminWebhook(cfg, userID, nameOverride)</c>. Both consumers here compare
    /// the strings to <c>humans.id</c>, a webhook URL, so a name-keyed delegate got a nav item off a
    /// non-empty list, an empty My Webhooks table, and a 403 from impersonate. See #797.
    /// <para>
    /// A grant naming no webhook at all is dropped rather than carried. It could never match a human row
    /// downstream, and carrying it renders a nav item onto a page that has nothing to show. When the
    /// lookup itself fails the raw set is kept and the answer is marked unresolved, so a database blip
    /// does not strip a legitimate delegate for the cache TTL (#667).
    /// </para>
    /// </remarks>
    private async Task<(string[] Canonical, bool Readable)> CanonicaliseAsync(HashSet<string> managed)
    {
        IEnumerable<Core.Models.Human> webhooks;
        try
        {
            webhooks = await this._humanService.GetWebhooksAsync();
        }
        catch (Exception ex)
        {
            LogWebhookLookupFailed(this._logger, ex);
            return ([.. managed], false);
        }

        var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var byId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var webhook in webhooks)
        {
            byId[webhook.Id] = webhook.Id;
            if (!string.IsNullOrWhiteSpace(webhook.Name))
            {
                byName.TryAdd(webhook.Name, webhook.Id);
            }
        }

        var canonical = new HashSet<string>(StringComparer.Ordinal);
        foreach (var grant in managed)
        {
            if (byId.TryGetValue(grant, out var id) || byName.TryGetValue(grant, out id))
            {
                canonical.Add(id);
            }
            else
            {
                LogUnresolvedWebhookGrant(this._logger, grant);
            }
        }

        return ([.. canonical], true);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to fetch Poracle config for admin check for {UserId}.")]
    private static partial void LogPoracleConfigFetchFailed(ILogger logger, Exception ex, string userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to fetch administration roles for {UserId}.")]
    private static partial void LogAdminRolesFetchFailed(ILogger logger, Exception ex, string userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to fetch webhook delegates for {UserId}.")]
    private static partial void LogPwebDelegatesFetchFailed(ILogger logger, Exception ex, string userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to look up webhooks while resolving delegated grants.")]
    private static partial void LogWebhookLookupFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Delegated webhook grant {Grant} names neither a webhook id nor a webhook name; ignoring it.")]
    private static partial void LogUnresolvedWebhookGrant(ILogger logger, string grant);
}
