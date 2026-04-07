using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Pgan.PoracleWebNet.Core.Abstractions.Services;

namespace Pgan.PoracleWebNet.Core.Services;

public partial class PokemonAvailabilityService(IGolbatApiProxy golbatApiProxy, IMemoryCache memoryCache, ILogger<PokemonAvailabilityService> logger) : IPokemonAvailabilityService
{
    private const string CacheKey = "golbat_available_pokemon";
    private static readonly TimeSpan s_cacheDuration = TimeSpan.FromMinutes(5);

    private readonly IGolbatApiProxy _golbatApiProxy = golbatApiProxy;
    private readonly IMemoryCache _memoryCache = memoryCache;
    private readonly ILogger<PokemonAvailabilityService> _logger = logger;

    private IReadOnlyList<int>? _lastKnownGood;

    public async Task<IReadOnlyList<int>> GetAvailablePokemonIdsAsync(CancellationToken cancellationToken = default)
    {
        if (this._memoryCache.TryGetValue(CacheKey, out IReadOnlyList<int>? cached) && cached is not null)
        {
            return cached;
        }

        try
        {
            var ids = await this._golbatApiProxy.GetAvailablePokemonAsync(cancellationToken);

            if (ids.Count > 0)
            {
                this._memoryCache.Set(CacheKey, ids, new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = s_cacheDuration
                });
                this._lastKnownGood = ids;
                LogAvailablePokemonFetched(this._logger, ids.Count);
            }

            return ids;
        }
        catch (Exception ex)
        {
            LogAvailablePokemonFetchFailed(this._logger, ex);

            if (this._lastKnownGood is not null)
            {
                LogUsingLastKnownGood(this._logger, this._lastKnownGood.Count);
                return this._lastKnownGood;
            }

            return [];
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Fetched {Count} available Pokemon IDs from Golbat")]
    private static partial void LogAvailablePokemonFetched(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to fetch available Pokemon from Golbat")]
    private static partial void LogAvailablePokemonFetchFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "Using last known good available Pokemon list ({Count} IDs)")]
    private static partial void LogUsingLastKnownGood(ILogger logger, int count);
}
