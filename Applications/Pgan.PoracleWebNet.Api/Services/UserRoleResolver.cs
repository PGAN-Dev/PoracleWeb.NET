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
    IWebhookDelegateService webhookDelegateService,
    IOptions<PoracleSettings> poracleSettings,
    IMemoryCache cache,
    ILogger<UserRoleResolver> logger) : IUserRoleResolver
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(1);

    private readonly IMemoryCache _cache = cache;
    private readonly ILogger<UserRoleResolver> _logger = logger;
    private readonly IPoracleApiProxy _poracleApiProxy = poracleApiProxy;
    private readonly PoracleSettings _poracleSettings = poracleSettings.Value;
    private readonly IWebhookDelegateService _webhookDelegateService = webhookDelegateService;

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
            rolesReadable = false;
        }

        if (isAdmin)
        {
            return new UserRoles(true, null);
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
            LogPwebDelegatesFetchFailed(this._logger, ex, userId);
        }

        return new UserRoles(false, managed.Count > 0 ? [.. managed] : null, configReadable && rolesReadable);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to fetch Poracle config for admin check for {UserId}.")]
    private static partial void LogPoracleConfigFetchFailed(ILogger logger, Exception ex, string userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to fetch administration roles for {UserId}.")]
    private static partial void LogAdminRolesFetchFailed(ILogger logger, Exception ex, string userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to fetch webhook delegates for {UserId}.")]
    private static partial void LogPwebDelegatesFetchFailed(ILogger logger, Exception ex, string userId);
}
