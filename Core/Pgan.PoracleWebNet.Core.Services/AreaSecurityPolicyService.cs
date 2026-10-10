using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Pgan.PoracleWebNet.Core.Abstractions.Services;

namespace Pgan.PoracleWebNet.Core.Services;

/// <inheritdoc cref="IAreaSecurityPolicyService" />
public sealed partial class AreaSecurityPolicyService(
    IPoracleApiProxy poracleApiProxy,
    IMemoryCache cache,
    ILogger<AreaSecurityPolicyService> logger) : IAreaSecurityPolicyService
{
    private const string CacheKey = "area_security:confirmed_disabled";

    // Matches SiteSettingService/UpstreamFeatureFlagService. This is a restart-scoped config.toml
    // value upstream, so even five minutes is generous -- but a stale "confirmed disabled" could
    // outlive an operator switching area_security on, which is exactly the direction this must not
    // drift in. Kept at five minutes anyway for now: shortening it only helps the one admin who
    // flips the setting and immediately starts drawing geofences inside the cache window, and this
    // app runs no write path during that window that both consults this cache and needs the new
    // value sooner than the next unrelated restart or request five minutes out.
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly IPoracleApiProxy _poracleApiProxy = poracleApiProxy;
    private readonly IMemoryCache _cache = cache;
    private readonly ILogger<AreaSecurityPolicyService> _logger = logger;

    /// <inheritdoc />
    public async Task<bool> IsConfirmedDisabledAsync()
    {
        if (this._cache.TryGetValue<bool>(CacheKey, out var cached))
        {
            return cached;
        }

        var confirmed = await this.ProbeAsync();
        this._cache.Set(CacheKey, confirmed, CacheTtl);
        return confirmed;
    }

    private async Task<bool> ProbeAsync()
    {
        try
        {
            return await this._poracleApiProxy.GetAreaSecurityEnabledAsync() == false;
        }
        catch (Exception ex)
        {
            LogProbeFailed(this._logger, ex);
            return false;
        }
    }

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Could not read area_security.enabled from Poracle; treating setAreas's trusted flag as unsafe for the community-restriction case")]
    private static partial void LogProbeFailed(ILogger logger, Exception exception);
}
