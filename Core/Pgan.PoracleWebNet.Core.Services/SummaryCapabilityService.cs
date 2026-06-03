using Microsoft.Extensions.Caching.Memory;
using Pgan.PoracleWebNet.Core.Abstractions.Services;

namespace Pgan.PoracleWebNet.Core.Services;

/// <summary>
/// Resolves whether the upstream PoracleNG deployment has quest summary delivery enabled.
/// The capability is a deployment property derived from the config proxy
/// (<c>tracking.quest_summary_enabled</c>) and surfaced as a 200-body boolean — never inferred
/// from a 503. The value (including <c>false</c>) is cached server-wide for 5 minutes.
/// Any fault while reading the config degrades to <c>false</c> (graceful degradation).
/// </summary>
public class SummaryCapabilityService(IPoracleApiProxy poracleApiProxy, IMemoryCache cache) : ISummaryCapabilityService
{
    private const string CacheKey = "summary_capability:quest";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly IPoracleApiProxy _poracleApiProxy = poracleApiProxy;
    private readonly IMemoryCache _cache = cache;

    public async Task<bool> IsQuestSummaryEnabledAsync()
    {
        if (this._cache.TryGetValue(CacheKey, out bool cached))
        {
            return cached;
        }

        var enabled = await this.ProbeConfigAsync();
        this._cache.Set(CacheKey, enabled, CacheTtl);
        return enabled;
    }

    private async Task<bool> ProbeConfigAsync()
    {
        try
        {
            var config = await this._poracleApiProxy.GetConfigAsync();
            return config?.QuestSummaryEnabled ?? false;
        }
        catch
        {
            return false;
        }
    }
}
