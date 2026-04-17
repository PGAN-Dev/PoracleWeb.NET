using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

public partial class SiteSettingService(
    ISiteSettingRepository repository,
    IMemoryCache cache,
    ILogger<SiteSettingService> logger) : ISiteSettingService
{
    /// <summary>
    /// Per-key cache TTL. Site settings change rarely (admin action) and writes invalidate
    /// explicitly, so this is just a safety net for orphaned entries. The dashboard fans out
    /// across ~10 alarm endpoints in parallel — with the controller filter calling
    /// <see cref="GetBoolAsync"/> on each, an uncached path would add 10 MySQL roundtrips per
    /// page load.
    ///
    /// <para>Known race: a slow read started before <see cref="CreateOrUpdateAsync"/> can populate
    /// the post-invalidation cache with the pre-write value, leaving stale data for up to the TTL.
    /// Acceptable trade-off — settings are admin-driven and infrequent, the staleness self-heals,
    /// and the alternative (per-read locking) would serialize a hot read path. Worst case: a small
    /// window where a user can still create alarms moments after admin disables the feature.</para>
    /// </summary>
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);
    private const string CacheKeyPrefix = "site_setting:";

    private readonly ISiteSettingRepository _repository = repository;
    private readonly IMemoryCache _cache = cache;
    private readonly ILogger<SiteSettingService> _logger = logger;

    private static string CacheKey(string key) => CacheKeyPrefix + key;

    /// <summary>
    /// Keys that are safe to expose via the unauthenticated public endpoint.
    /// Expand this set as new public-facing settings are added.
    /// </summary>
    private static readonly HashSet<string> PublicKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "custom_title",
        "enable_discord",
        "enable_telegram",
        "favicon_url",
        "signup_url",
    };

    public async Task<IEnumerable<SiteSetting>> GetAllAsync() => await this._repository.GetAllAsync();

    public async Task<IEnumerable<SiteSetting>> GetByCategoryAsync(string category) =>
        await this._repository.GetByCategoryAsync(category);

    public async Task<IEnumerable<SiteSetting>> GetPublicAsync()
    {
        var results = new List<SiteSetting>();
        foreach (var key in PublicKeys)
        {
            var setting = await this._repository.GetByKeyAsync(key);
            if (setting is not null)
            {
                results.Add(setting);
            }
        }

        return results;
    }

    public async Task<SiteSetting?> GetByKeyAsync(string key)
    {
        if (this._cache.TryGetValue<SiteSetting?>(CacheKey(key), out var cached))
        {
            return cached;
        }

        var setting = await this._repository.GetByKeyAsync(key);
        // Cache nulls too — "key doesn't exist" is a stable answer until something writes it.
        this._cache.Set(CacheKey(key), setting, CacheTtl);
        return setting;
    }

    public async Task<string?> GetValueAsync(string key)
    {
        var setting = await this.GetByKeyAsync(key);
        return setting?.Value;
    }

    public async Task<bool> GetBoolAsync(string key)
    {
        var value = await this.GetValueAsync(key);
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<SiteSetting> CreateOrUpdateAsync(SiteSetting setting)
    {
        var result = await this._repository.CreateOrUpdateAsync(setting);
        this._cache.Remove(CacheKey(setting.Key));
        LogSettingUpdated(this._logger, setting.Key, setting.Category);
        return result;
    }

    public async Task<bool> DeleteAsync(string key)
    {
        var result = await this._repository.DeleteAsync(key);
        this._cache.Remove(CacheKey(key));
        if (result)
        {
            LogSettingDeleted(this._logger, key);
        }

        return result;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Site setting '{Key}' updated (category: {Category})")]
    private static partial void LogSettingUpdated(ILogger logger, string key, string category);

    [LoggerMessage(Level = LogLevel.Information, Message = "Site setting '{Key}' deleted")]
    private static partial void LogSettingDeleted(ILogger logger, string key);
}
