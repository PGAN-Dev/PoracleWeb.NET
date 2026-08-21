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
/// Two upstream reads are needed because the flags are split across two shapes. The
/// <c>disabledHooks</c> array on <c>GET /api/config/poracleWeb</c> covers the nine webhook types in
/// PoracleNG's <c>hookTypes</c> list; <c>general.disable_fort_update</c> on
/// <c>GET /api/config/values</c> covers fort changes, which PoracleNG enforces in the processor and
/// the bot but leaves out of the array.
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
            return None;
        }

        try
        {
            if (await this._poracleApiProxy.GetFortUpdateDisabledAsync() == true)
            {
                keys.Add(DisableFeatureKeys.FortChanges);
            }
        }
        catch (Exception ex)
        {
            // Independent degradation: a missing /api/config/values must not discard the hook list
            // we already have. PoracleJS does not serve that route at all.
            LogProbeFailed(this._logger, "general.disable_fort_update", ex);
        }

        return keys;
    }

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Could not read '{Source}' from Poracle; leaving the site settings in sole charge")]
    private static partial void LogProbeFailed(ILogger logger, string source, Exception exception);
}
