using Microsoft.Extensions.Caching.Memory;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

/// <summary>
/// Reads <c>shiny_rates.url</c> and <c>player_accounts.self_service</c> from PoracleNG's
/// <c>/api/config/values</c>, cached server-wide for 5 minutes (failures included, so a PoracleNG
/// outage is not asked again on every request). Fails closed: any fault, or an absent key, reads as
/// not configured and self-service off.
/// </summary>
/// <remarks>
/// <c>shiny_rates.url</c> is a plain string in PoracleNG's config schema and is not redacted (only the
/// neighbouring <c>token</c> is, as "****"), so "configured" is simply "non-blank after trimming".
/// </remarks>
public class PersonalShinyConfigService(IPoracleApiProxy poracleApiProxy, IMemoryCache cache) : IPersonalShinyConfigService
{
    private const string CacheKey = "personal_shiny_config";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly IPoracleApiProxy _poracleApiProxy = poracleApiProxy;
    private readonly IMemoryCache _cache = cache;

    public async Task<PersonalShinyConfig> GetAsync()
    {
        if (this._cache.TryGetValue(CacheKey, out PersonalShinyConfig? cached) && cached is not null)
        {
            return cached;
        }

        var config = await this.ProbeAsync();
        this._cache.Set(CacheKey, config, CacheTtl);
        return config;
    }

    private async Task<PersonalShinyConfig> ProbeAsync()
    {
        try
        {
            var url = await this._poracleApiProxy.GetShinyRatesUrlAsync();
            var selfService = await this._poracleApiProxy.GetPlayerAccountsSelfServiceAsync();
            return new PersonalShinyConfig(!string.IsNullOrWhiteSpace(url), selfService ?? false);
        }
        catch
        {
            return new PersonalShinyConfig(false, false);
        }
    }
}
