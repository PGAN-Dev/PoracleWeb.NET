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
/// With 5.4.0 the minimum that read is gone; do not bring it back for a server that is not supported.
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
/// <c>disable_showcase</c> is read beside the hook list (<c>general.disable_showcase = true</c> turns
/// Pokestop Events off) and fails open like the rest.
/// </para>
/// <para>
/// <c>disable_nominatim</c> comes from the same config read: PoracleNG with no geocoder (<c>provider</c>
/// empty or <c>none</c>) forces address search and reverse lookups off. See #1111.
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

            if (HasNoGeocoder(config?.Provider))
            {
                keys.Add(DisableFeatureKeys.Geocoding);
            }
        }
        catch (Exception ex)
        {
            LogProbeFailed(this._logger, "disabledHooks", ex);

            // Partial, not none. A hook list we could not read must not discard the showcase read below it.
        }

        await this.ReadShowcaseFlagAsync(keys);

        return keys;
    }

    /// <summary>
    /// True when PoracleNG reports <c>[geocoding] provider</c> as empty or <c>none</c>: it builds no
    /// geocoder then, and every address search and reverse lookup answers 503 (#1111). Keyed on the
    /// known-off values, not on a list of known providers, so a provider PoracleNG adds later is left on.
    /// Null (PoracleJS, which sends no field) is no opinion.
    /// </summary>
    private static bool HasNoGeocoder(string? provider) =>
        provider is not null
        && (provider.Trim().Length == 0 || provider.Trim().Equals("none", StringComparison.OrdinalIgnoreCase));

    /// <summary>Pokestop Events are off only when Poracle says so; absent or unreadable leaves them on.</summary>
    private async Task ReadShowcaseFlagAsync(HashSet<string> keys)
    {
        try
        {
            if (await this._poracleApiProxy.GetShowcaseDisabledAsync() is true)
            {
                keys.Add(DisableFeatureKeys.PokestopEvents);
            }
        }
        catch (Exception ex)
        {
            LogProbeFailed(this._logger, "general.disable_showcase", ex);
        }
    }

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Could not read '{Source}' from Poracle; leaving the site settings in sole charge")]
    private static partial void LogProbeFailed(ILogger logger, string source, Exception exception);
}
