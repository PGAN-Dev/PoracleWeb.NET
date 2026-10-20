using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

/// <summary>
/// Reads Poracle's own per-type disable flags and reports them as <c>disable_*</c> keys.
/// </summary>
/// <remarks>
/// <para>
/// The <c>disabledHooks</c> array on <c>GET /api/config/poracleWeb</c> carries the flags, fort changes
/// included. Up to PoracleNG 5.1.0 it left <c>fort</c> out, and this class made a second read of
/// <c>general.disable_fort_update</c> from <c>GET /api/config/values</c> for any server that did not
/// report <c>availableLanguages</c> (the field 5.2.1 added in the same release, jfberry/PoracleNG#197).
/// With 5.2.1 the minimum that read is gone; do not bring it back for a server that is not supported.
/// </para>
/// <para>
/// The result is cached server-wide for five minutes, matching <c>SiteSettingService</c>. Upstream
/// this is a restart-scoped value read from <c>config.toml</c>, so even five minutes is generous —
/// but the gate is on the hot path (the dashboard fans out across ~10 alarm endpoints) and must not
/// add two HTTP round-trips per request.
/// </para>
/// <para>
/// <strong>It fails open, deliberately.</strong> Any fault, timeout, or absent field yields an empty
/// set, leaving the site settings in sole charge. Failing closed would let a Poracle outage disable
/// every alarm type for everyone, which is a far worse failure than the one this feature prevents.
/// </para>
/// <para>
/// <c>disable_showcase</c> is the single exception, and <see cref="ProbePokestopEventsAsync"/> says why.
/// </para>
/// </remarks>
public sealed partial class UpstreamFeatureFlagService(
    IPoracleApiProxy poracleApiProxy,
    IMemoryCache cache,
    ILogger<UpstreamFeatureFlagService> logger) : IUpstreamFeatureFlagService
{
    private const string CacheKey = "upstream_disabled_keys";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);
    private static readonly IReadOnlySet<string> None = new HashSet<string>(StringComparer.Ordinal);

    private readonly IPoracleApiProxy _poracleApiProxy = poracleApiProxy;
    private readonly IMemoryCache _cache = cache;
    private readonly ILogger<UpstreamFeatureFlagService> _logger = logger;

    public async Task<IReadOnlySet<string>> GetDisabledKeysAsync()
    {
        if (this._cache.TryGetValue<IReadOnlySet<string>>(CacheKey, out var cached) && cached is not null)
        {
            return cached;
        }

        var keys = await this.ProbeAsync();
        this._cache.Set(CacheKey, keys, CacheTtl);
        return keys;
    }

    private async Task<IReadOnlySet<string>> ProbeAsync()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);

        try
        {
            var config = await this._poracleApiProxy.GetConfigAsync();
            foreach (var key in PoracleDisabledHookMap.ToDisableKeys(config?.DisabledHooks))
            {
                keys.Add(key);
            }
        }
        catch (Exception ex)
        {
            LogProbeFailed(this._logger, "disabledHooks", ex);

            // Partial, not none. A hook list we could not read must not discard the probe below it,
            // which is the only thing keeping the SPA off a route that does not exist on an older server.
        }

        await this.ProbePokestopEventsAsync(keys);

        return keys;
    }

    /// <summary>
    /// Decides whether the Pokestop Events surface is available, and is <strong>the one probe in this
    /// class that fails closed</strong>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only an explicit <c>general.disable_showcase = false</c> opens the gate. Absent means the server
    /// predates the option, and every such server also 404s the v2 <c>incident</c> route the page is
    /// built on — verified: 5.1.0 has neither the config key nor the route, 5.2.1 has both. Unreadable
    /// means we cannot tell, and a server whose config we cannot read is not one we can send v2 writes
    /// to either.
    /// </para>
    /// <para>
    /// This inverts the class's stated contract deliberately, and the reason is narrower than it looks:
    /// failing open elsewhere leaves a working page switched on, while failing open here produces a page
    /// whose every call 404s. The cost is bounded by the five-minute cache. Do not "fix" it back.
    /// </para>
    /// </remarks>
    private async Task ProbePokestopEventsAsync(HashSet<string> keys)
    {
        try
        {
            if (await this._poracleApiProxy.GetShowcaseDisabledAsync() is false)
            {
                return;
            }
        }
        catch (Exception ex)
        {
            LogProbeFailed(this._logger, "general.disable_showcase", ex);
        }

        keys.Add(DisableFeatureKeys.PokestopEvents);
    }

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Could not read '{Source}' from Poracle; leaving the site settings in sole charge")]
    private static partial void LogProbeFailed(ILogger logger, string source, Exception exception);
}
