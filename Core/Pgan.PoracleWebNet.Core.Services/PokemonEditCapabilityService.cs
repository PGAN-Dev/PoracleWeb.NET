using Microsoft.Extensions.Caching.Memory;
using Pgan.PoracleWebNet.Core.Abstractions.Services;

namespace Pgan.PoracleWebNet.Core.Services;

/// <summary>
/// Resolves whether the upstream PoracleNG deployment honours the pokemon <c>edit</c> bit
/// (jfberry/PoracleNG#246, merged 2026-10-08 into <c>develop</c>, not yet in a tagged release). The
/// capability is a deployment property derived from the config proxy (<c>tracking.pokemon_edit</c>)
/// and surfaced as a 200-body boolean -- never inferred from a 503. Before #246 the processor accepted
/// the bit (commands, v1 and v2) but never acted on it, so a server that predates it, or an operator
/// who has turned the setting off, must answer <c>false</c> here rather than offering a control that is
/// a no-op. The value (including <c>false</c>) is cached server-wide for 5 minutes. Any fault while
/// reading the config degrades to <c>false</c> (graceful degradation), mirroring
/// <see cref="SummaryCapabilityService"/>.
/// </summary>
public class PokemonEditCapabilityService(IPoracleApiProxy poracleApiProxy, IMemoryCache cache) : IPokemonEditCapabilityService
{
    private const string CacheKey = "pokemon_edit_capability";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly IPoracleApiProxy _poracleApiProxy = poracleApiProxy;
    private readonly IMemoryCache _cache = cache;

    public async Task<bool> IsPokemonEditEnabledAsync()
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
            return await this._poracleApiProxy.GetPokemonEditEnabledAsync() ?? false;
        }
        catch
        {
            return false;
        }
    }
}
