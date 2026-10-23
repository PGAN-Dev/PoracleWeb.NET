using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Pgan.PoracleWebNet.Api.Configuration;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Api.Services;

/// <summary>
/// A user's admin status and the destinations they may administer, resolved from live sources.
/// </summary>
/// <param name="IsAdmin">Whether the user is an admin. Meaningless when <paramref name="Resolved"/> is false.</param>
/// <param name="ManagedDestinations">
/// The <c>humans.id</c> of every destination (webhook, channel, thread, group or topic) the user may
/// administer, or null.
/// </param>
/// <param name="Resolved">
/// False when a source we needed was unreachable. Callers that stamp roles onto a token must treat
/// this as "do not change the claim" rather than as "not an admin" -- a PoracleNG blip during a
/// profile switch otherwise stripped admin for the rest of the session. See #656.
/// </param>
public readonly record struct UserRoles(bool IsAdmin, string[]? ManagedDestinations, bool Resolved = true);

/// <summary>
/// Resolves admin status and delegated destinations from the configured admin list, Poracle's config,
/// PoracleNG's <c>getAdministrationRoles</c>, and PoracleWeb's own delegate table.
/// </summary>
public interface IUserRoleResolver
{
    /// <summary>Resolves the user's current roles.</summary>
    Task<UserRoles> ResolveAsync(string userId);

    /// <summary>Drops the cached answer for each of these users, so the next resolve reads live.</summary>
    void Invalidate(params string[] userIds);

    /// <summary>Drops every cached answer.</summary>
    void InvalidateAll();
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
/// My Destinations nav item and get an empty page and a 403 (#626).
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
    IDestinationDirectory destinationDirectory,
    ISiteSettingService siteSettingService,
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
    private readonly IDestinationDirectory _destinationDirectory = destinationDirectory;
    private readonly ISiteSettingService _siteSettingService = siteSettingService;

    /// <summary>
    /// Site setting that lets PoracleNG's channel admin lists grant delegation. Absent means off.
    /// </summary>
    /// <remarks>
    /// <c>admin.discord.channels</c> and <c>admin.telegram.channels</c> list what a user may administer in
    /// the bot, which an operator may not mean as "may manage this destination's alarms here". So they
    /// count only when the operator opts in. See #1025.
    /// </remarks>
    public const string PoracleChannelsSettingKey = "delegation_poracle_channels";

    public async Task<UserRoles> ResolveAsync(string userId)
    {
        var cacheKey = this.CacheKey(userId);
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

    /// <remarks>
    /// Granting or revoking a delegate took up to the full minute to land, because the admin dialog wrote
    /// the table and left the cached answer where it was. Both sides of the grant are dropped: the
    /// delegate, whose <c>ManagedDestinations</c> changed, and the webhook, in case anything is keyed by it.
    /// </remarks>
    public void Invalidate(params string[] userIds)
    {
        ArgumentNullException.ThrowIfNull(userIds);
        foreach (var userId in userIds.Where(id => !string.IsNullOrEmpty(id)))
        {
            this._cache.Remove(this.CacheKey(userId));
        }
    }

    /// <remarks>
    /// For deleting a human, where the set of affected users is not knowable: a webhook's delegates can be
    /// named in PoracleJS's own config, by the webhook's name, and appear in no table here. Rather than
    /// enumerate the cache, the generation every key carries is moved on, so every existing entry misses
    /// and ages out on its own TTL. The generation lives in the shared cache, not on this instance, because
    /// the resolver is scoped and the next request has its own.
    /// </remarks>
    public void InvalidateAll() => Interlocked.Increment(ref this.Generation().Value);

    private string CacheKey(string userId) =>
        $"roles:{Volatile.Read(ref this.Generation().Value).ToString(System.Globalization.CultureInfo.InvariantCulture)}:{userId}";

    private RolesGeneration Generation() =>
        this._cache.GetOrCreate(GenerationKey, entry =>
        {
            entry.Priority = CacheItemPriority.NeverRemove;
            return new RolesGeneration();
        })!;

    private const string GenerationKey = "roles:generation";

    /// <summary>A mutable holder, so the counter can be moved on atomically in place.</summary>
    private sealed class RolesGeneration
    {
        public long Value;
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
            LogPoracleConfigFetchFailed(this._logger, ex, LogSafe.Id(userId));
            configReadable = false;
        }

        // Ask PoracleNG once for the delegated destinations, Discord guild roles included. Grants in
        // `managed` may name a destination by id or by name; `managedById` holds channel ids, which
        // PoracleNG reports as ids and which must never be read as a name.
        var managed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var managedById = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var rolesJson = await this._poracleHumanProxy.GetAdminRolesAsync(userId);
            if (!string.IsNullOrEmpty(rolesJson))
            {
                using var doc = JsonDocument.Parse(rolesJson);
                var root = doc.RootElement;

                // admin.discord.webhooks is the authoritative delegate list, keyed by id or by name.
                //
                // Two isAdmin branches used to sit here, one at the root and one under admin.discord.
                // Neither has ever fired: both API versions build this body from the same
                // adminRolesResult, whose only fields are channels, webhooks and users, and v2's schema
                // is additionalProperties:false so an isAdmin could not appear even by accident.
                // Admin status is resolved above, from the configured ids and Poracle's own config.
                // `users: true` is not read either: administering people is not delegation here.
                if (root.TryGetProperty("admin", out var adminEl) && adminEl.ValueKind == JsonValueKind.Object)
                {
                    var discordEl = adminEl.TryGetProperty("discord", out var d) ? d : default;
                    var telegramEl = adminEl.TryGetProperty("telegram", out var t) ? t : default;

                    AddStrings(discordEl, "webhooks", managed);

                    var channelIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    AddStrings(discordEl, "channels", channelIds);
                    AddStrings(telegramEl, "channels", channelIds);
                    if (channelIds.Count > 0 && await this.PoracleChannelsCountAsync())
                    {
                        managedById.UnionWith(channelIds);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogAdminRolesFetchFailed(this._logger, ex, LogSafe.Id(userId));
            rolesReadable = false;
        }

        // Also merge our own delegate table. It stores ids, and since #1025 any destination's id.
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
            LogPwebDelegatesFetchFailed(this._logger, ex, LogSafe.Id(userId));
            delegatesReadable = false;
        }

        if (managed.Count == 0 && managedById.Count == 0)
        {
            return new UserRoles(false, null, configReadable && rolesReadable && delegatesReadable);
        }

        var (canonical, humansReadable) = await this.CanonicaliseAsync(managed, managedById);

        return new UserRoles(false, canonical.Length > 0 ? canonical : null,
            configReadable && rolesReadable && delegatesReadable && humansReadable);
    }

    /// <summary>
    /// Rewrites each grant to the <c>humans.id</c> it names, dropping the ones that name nothing.
    /// </summary>
    /// <remarks>
    /// PoracleNG hands back whatever key the operator wrote in <c>[[discord.webhook_admins]] target</c>,
    /// and upstream that key may be a NAME -- the bot resolves it with <c>LookupWebhookByName</c> and
    /// authorises with <c>CanAdminWebhook(cfg, userID, nameOverride)</c>. Both consumers here compare the
    /// strings to <c>humans.id</c>, so a name-keyed delegate got a nav item off a non-empty list, an empty
    /// My Destinations table, and a 403 from impersonate. See #797.
    /// <para>
    /// Ids match any destination in the directory. Names match only the types upstream resolves by name
    /// (<see cref="HumanKind.NameAddressable"/>), so a thread or topic that shares a name with a webhook
    /// is never what the grant meant. A name shared across types goes to the webhook; shared by two or more
    /// webhooks, or by two or more other destinations and no webhook, it is dropped as ambiguous (#1038).
    /// Grants in <paramref name="byIdOnly"/>
    /// came from a list of ids and are never read as names. Rows that are not one of the six destination
    /// types are ignored outright. See #1025.
    /// </para>
    /// <para>
    /// A grant naming nothing is dropped rather than carried. It could never match a human row
    /// downstream, and carrying it renders a nav item onto a page that has nothing to show. When the
    /// lookup itself fails the raw set is kept and the answer is marked unresolved, so an outage does not
    /// strip a legitimate delegate for the cache TTL (#667).
    /// </para>
    /// </remarks>
    private async Task<(string[] Canonical, bool Readable)> CanonicaliseAsync(HashSet<string> managed, HashSet<string> byIdOnly)
    {
        IReadOnlyList<Human> destinations;
        try
        {
            destinations = await this._destinationDirectory.GetAsync();
        }
        catch (Exception ex)
        {
            LogDestinationLookupFailed(this._logger, ex);
            return ([.. managed.Union(byIdOnly, StringComparer.OrdinalIgnoreCase)], false);
        }

        // Upstream's name lookup has no ORDER BY, so which row a shared name lands on there is arbitrary.
        // Here it is fixed: a webhook wins, since every grant written before #1025 meant one. A name shared
        // by two or more webhooks, or by two or more other destinations and no webhook, names none of them:
        // picking one would hand over whichever row PoracleNG happened to list first (#1038).
        var webhookByName = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var otherByName = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var byId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // The directory asks for destination types only; this keeps a person out even if a server ignores
        // the filter and lists everyone.
        foreach (var destination in destinations.Where(d => HumanKind.IsKnownDestination(d.Type)))
        {
            byId[destination.Id] = destination.Id;
            if (string.IsNullOrWhiteSpace(destination.Name) || !HumanKind.NameAddressable.Contains(destination.Type!))
            {
                continue;
            }

            var byName = string.Equals(destination.Type, HumanKind.Webhook, StringComparison.OrdinalIgnoreCase)
                ? webhookByName
                : otherByName;
            if (byName.TryGetValue(destination.Name, out var ids))
            {
                ids.Add(destination.Id);
            }
            else
            {
                byName[destination.Name] = [destination.Id];
            }
        }

        var canonical = new HashSet<string>(StringComparer.Ordinal);
        foreach (var grant in managed)
        {
            if (byId.TryGetValue(grant, out var id))
            {
                canonical.Add(id);
            }
            else if (webhookByName.TryGetValue(grant, out var named) || otherByName.TryGetValue(grant, out named))
            {
                if (named.Count == 1)
                {
                    canonical.Add(named[0]);
                }
                else
                {
                    LogAmbiguousGrant(this._logger, LogSafe.Id(grant), named.Count);
                }
            }
            else
            {
                LogUnresolvedGrant(this._logger, LogSafe.Id(grant));
            }
        }

        foreach (var grant in byIdOnly)
        {
            if (byId.TryGetValue(grant, out var id))
            {
                canonical.Add(id);
            }
            else
            {
                LogUnresolvedGrant(this._logger, LogSafe.Id(grant));
            }
        }

        return ([.. canonical], true);
    }

    /// <summary>
    /// Whether the operator opted in to <see cref="PoracleChannelsSettingKey"/>. An unreadable setting
    /// counts as the default, off, and does not mark the answer unresolved: the opt-in only widens access,
    /// so failing closed withholds nothing a default install would have granted.
    /// </summary>
    private async Task<bool> PoracleChannelsCountAsync()
    {
        try
        {
            return await this._siteSettingService.GetBoolAsync(PoracleChannelsSettingKey);
        }
        catch (Exception ex)
        {
            LogChannelSettingReadFailed(this._logger, ex);
            return false;
        }
    }

    private static void AddStrings(JsonElement parent, string property, HashSet<string> into)
    {
        if (parent.ValueKind != JsonValueKind.Object
            || !parent.TryGetProperty(property, out var array)
            || array.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } value)
            {
                into.Add(value);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to fetch Poracle config for admin check for {UserId}.")]
    private static partial void LogPoracleConfigFetchFailed(ILogger logger, Exception ex, string userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to fetch administration roles for {UserId}.")]
    private static partial void LogAdminRolesFetchFailed(ILogger logger, Exception ex, string userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to fetch webhook delegates for {UserId}.")]
    private static partial void LogPwebDelegatesFetchFailed(ILogger logger, Exception ex, string userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to look up destinations while resolving delegated grants.")]
    private static partial void LogDestinationLookupFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Could not read the delegation_poracle_channels setting; treating Poracle's channel lists as off.")]
    private static partial void LogChannelSettingReadFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Delegated grant {Grant} names no registered destination; ignoring it.")]
    private static partial void LogUnresolvedGrant(ILogger logger, string grant);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Delegated grant {Grant} names {Count} destinations with nothing to choose between them; ignoring it as ambiguous.")]
    private static partial void LogAmbiguousGrant(ILogger logger, string grant, int count);
}
