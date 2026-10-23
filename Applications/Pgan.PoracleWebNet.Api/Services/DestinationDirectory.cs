using Microsoft.Extensions.Caching.Memory;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Api.Services;

/// <summary>
/// Every registered destination (webhook, channel, thread, group, topic), read once a minute.
/// </summary>
public interface IDestinationDirectory
{
    /// <summary>Lists every destination PoracleNG knows. Throws when any read fails; nothing is cached then.</summary>
    Task<IReadOnlyList<Human>> GetAsync();

    /// <summary>Drops the cached list so the next read goes back to PoracleNG.</summary>
    void Invalidate();
}

/// <summary>
/// PoracleNG lists humans one type at a time, so this asks once per entry in
/// <see cref="HumanKind.Destinations"/> and never asks for people. A failed read throws and is not
/// cached, so an outage cannot read as "no destinations exist".
/// </summary>
public sealed class DestinationDirectory(IPoracleHumanProxy humanProxy, IMemoryCache cache) : IDestinationDirectory
{
    private const string CacheKey = "destinations:directory";

    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(1);

    // Process-wide, like PoracleServerProfileService: this class is scoped, so an instance field would
    // serialise nothing, and the cache it guards is the shared singleton.
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private readonly IPoracleHumanProxy _humanProxy = humanProxy;
    private readonly IMemoryCache _cache = cache;

    public async Task<IReadOnlyList<Human>> GetAsync()
    {
        if (this.TryGetCached(out var cached))
        {
            return cached;
        }

        await Gate.WaitAsync();

        try
        {
            if (this.TryGetCached(out cached))
            {
                return cached;
            }

            var lists = await Task.WhenAll(HumanKind.Destinations.Select(t => this._humanProxy.ListHumansAsync(type: t)));
            IReadOnlyList<Human> all = [.. lists.SelectMany(l => l)];
            this._cache.Set(CacheKey, all, CacheFor);

            return all;
        }
        finally
        {
            Gate.Release();
        }
    }

    public void Invalidate() => this._cache.Remove(CacheKey);

    private bool TryGetCached(out IReadOnlyList<Human> list)
    {
        if (this._cache.TryGetValue(CacheKey, out IReadOnlyList<Human>? cached) && cached is not null)
        {
            list = cached;
            return true;
        }

        list = [];
        return false;
    }
}
