using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Pgan.PoracleWebNet.Core.Abstractions.Services;

namespace Pgan.PoracleWebNet.Core.Services;

public partial class MasterDataService(
    IMemoryCache cache,
    IHttpClientFactory httpClientFactory,
    ILogger<MasterDataService> logger) : IMasterDataService
{
    private readonly IMemoryCache _cache = cache;
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly ILogger<MasterDataService> _logger = logger;

    private const string PokemonCacheKey = "MasterData_Pokemon";
    private const string ItemCacheKey = "MasterData_Items";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(24);

    private const string MasterfileUrl =
        "https://raw.githubusercontent.com/WatWowMap/Masterfile-Generator/master/master-latest-poracle.json";

    private bool _initialized;

    public async Task<string?> GetPokemonDataAsync()
    {
        await this.EnsureInitializedAsync();
        this._cache.TryGetValue(PokemonCacheKey, out string? data);
        return data;
    }

    public async Task<string?> GetItemDataAsync()
    {
        await this.EnsureInitializedAsync();
        this._cache.TryGetValue(ItemCacheKey, out string? data);
        return data;
    }

    public async Task RefreshCacheAsync()
    {
        try
        {
            var client = this._httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("PGAN-PoracleWeb/1.0");
            client.Timeout = TimeSpan.FromSeconds(30);

            LogFetchingMasterData(this._logger);
            var json = await client.GetStringAsync(MasterfileUrl);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Build pokemon name map: { "1": "Bulbasaur", "2": "Ivysaur", ... }
            // Masterfile keys are "{pokemonId}_{formId}", e.g. "1_0" for Bulbasaur
            var pokemonMap = new Dictionary<string, string>();
            if (root.TryGetProperty("monsters", out var monsters))
            {
                foreach (var entry in monsters.EnumerateObject())
                {
                    // Key is "pokemonId_formId" - extract just the pokemon ID
                    var parts = entry.Name.Split('_');
                    var pokemonId = parts[0];

                    if (entry.Value.TryGetProperty("name", out var nameProp))
                    {
                        var name = nameProp.GetString() ?? pokemonId;
                        // Only store the first (base form) name per pokemon ID
                        pokemonMap.TryAdd(pokemonId, name);
                    }
                }
            }
            this._cache.Set(PokemonCacheKey, JsonSerializer.Serialize(pokemonMap), CacheDuration);
            LogCachedPokemonEntries(this._logger, pokemonMap.Count);

            // Build item name map
            var itemMap = new Dictionary<string, string>();
            if (root.TryGetProperty("items", out var items))
            {
                foreach (var entry in items.EnumerateObject())
                {
                    var id = entry.Name;
                    var name = id;
                    if (entry.Value.TryGetProperty("name", out var nameProp))
                    {
                        name = nameProp.GetString() ?? id;
                    }
                    else if (entry.Value.ValueKind == JsonValueKind.String)
                    {
                        name = entry.Value.GetString() ?? id;
                    }

                    itemMap[id] = name;
                }
            }
            this._cache.Set(ItemCacheKey, JsonSerializer.Serialize(itemMap), CacheDuration);
            LogCachedItemEntries(this._logger, itemMap.Count);
        }
        catch (Exception ex)
        {
            LogRefreshCacheFailed(this._logger, ex);
        }
    }

    private async Task EnsureInitializedAsync()
    {
        if (this._initialized)
        {
            return;
        }

        this._initialized = true;
        await this.RefreshCacheAsync();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Fetching Pokemon masterdata from GitHub...")]
    private static partial void LogFetchingMasterData(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Cached {Count} pokemon entries.")]
    private static partial void LogCachedPokemonEntries(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Cached {Count} item entries.")]
    private static partial void LogCachedItemEntries(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to refresh master data cache.")]
    private static partial void LogRefreshCacheFailed(ILogger logger, Exception ex);
}
