using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models.Pvp;

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
    private const string MoveCacheKey = "MasterData_Moves";
    private const string MonsterCacheKey = "MasterData_Monsters";
    private const string BaseStatsCacheKey = "MasterData_BaseStats";
    private const string CostumeCacheKey = "MasterData_Costumes";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(24);

    private const string MasterfileUrl =
        "https://raw.githubusercontent.com/WatWowMap/Masterfile-Generator/master/master-latest-poracle.json";

    // The poracle-shaped masterfile carries no costume map, so costume names come from the raw one -
    // the same file PoracleNG downloads for its own costume lookups. English only: PoracleNG translates
    // costume names internally from gamelocale keys but exposes no endpoint serving them, verified
    // against 5.2.1 (GET /api/masterdata/costumes answers 404).
    private const string RawMasterfileUrl =
        "https://raw.githubusercontent.com/WatWowMap/Masterfile-Generator/master/master-latest-raw.json";

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

    public async Task<string?> GetMoveDataAsync()
    {
        await this.EnsureInitializedAsync();
        this._cache.TryGetValue(MoveCacheKey, out string? data);
        return data;
    }

    public async Task<string?> GetMonsterDataAsync()
    {
        await this.EnsureInitializedAsync();
        this._cache.TryGetValue(MonsterCacheKey, out string? data);
        return data;
    }

    public async Task<string?> GetCostumeDataAsync()
    {
        await this.EnsureInitializedAsync();
        this._cache.TryGetValue(CostumeCacheKey, out string? data);
        return data;
    }

    public async Task<BaseStats?> GetBaseStatsAsync(int pokemonId, int form)
    {
        await this.EnsureInitializedAsync();
        if (!this._cache.TryGetValue(BaseStatsCacheKey, out Dictionary<string, BaseStats>? map) || map is null)
        {
            return null;
        }

        if (map.TryGetValue($"{pokemonId}_{form}", out var stats))
        {
            return stats;
        }

        if (form != 0 && map.TryGetValue($"{pokemonId}_0", out var fallback))
        {
            return fallback;
        }

        return null;
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
            var baseStatsMap = new Dictionary<string, BaseStats>();
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

                    if (entry.Value.TryGetProperty("stats", out var statsProp)
                        && statsProp.ValueKind == JsonValueKind.Object
                        && statsProp.TryGetProperty("baseAttack", out var atkProp)
                        && statsProp.TryGetProperty("baseDefense", out var defProp)
                        && statsProp.TryGetProperty("baseStamina", out var staProp))
                    {
                        baseStatsMap[entry.Name] = new BaseStats(
                            atkProp.GetInt32(),
                            defProp.GetInt32(),
                            staProp.GetInt32());
                    }
                }
            }
            // The whole monster map is kept verbatim as the English fallback for
            // GET /api/masterdata/monsters, which normally serves PoracleNG's localized version.
            if (monsters.ValueKind == JsonValueKind.Object)
            {
                this._cache.Set(MonsterCacheKey, monsters.GetRawText(), CacheDuration);
            }

            this._cache.Set(PokemonCacheKey, JsonSerializer.Serialize(pokemonMap), CacheDuration);
            this._cache.Set(BaseStatsCacheKey, baseStatsMap, CacheDuration);
            LogCachedPokemonEntries(this._logger, pokemonMap.Count);
            LogCachedBaseStats(this._logger, baseStatsMap.Count);

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

            // Build move name map. Masterfile entries are { "13": { "name": "Wrap", "type": "Normal" } };
            // only the name is needed, so this collapses to id -> name like the item map.
            var moveMap = new Dictionary<string, string>();
            if (root.TryGetProperty("moves", out var moves))
            {
                foreach (var entry in moves.EnumerateObject())
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

                    moveMap[id] = name;
                }
            }
            this._cache.Set(MoveCacheKey, JsonSerializer.Serialize(moveMap), CacheDuration);
            LogCachedMoveEntries(this._logger, moveMap.Count);
        }
        catch (Exception ex)
        {
            LogRefreshCacheFailed(this._logger, ex);
        }

        await this.RefreshCostumeCacheAsync();
    }

    /// <summary>
    /// Costume id to name, from the raw masterfile.
    /// </summary>
    /// <remarks>
    /// Its own request and its own try/catch on purpose: pokemon, items, moves and base stats all come
    /// from one fetch, and folding a second URL into that try would let a hiccup on this one blank all
    /// four. A costume failure only costs the names - the "any costume" and "no costume" choices are
    /// sentinels the UI owns and keep working.
    /// </remarks>
    private async Task RefreshCostumeCacheAsync()
    {
        try
        {
            var client = this._httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("PGAN-PoracleWeb/1.0");
            client.Timeout = TimeSpan.FromSeconds(30);

            var json = await client.GetStringAsync(RawMasterfileUrl);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("costumes", out var costumes) || costumes.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            var costumeMap = new Dictionary<string, string>();
            foreach (var entry in costumes.EnumerateObject())
            {
                // Id 0 is "Unset", the wire's word for "no costume". The UI offers that as its own
                // choice in the user's words, so listing it again as a named costume would be two
                // controls for one state.
                if (entry.Name == "0")
                {
                    continue;
                }

                if (entry.Value.ValueKind == JsonValueKind.Object
                    && entry.Value.TryGetProperty("name", out var nameProp)
                    && nameProp.GetString() is { Length: > 0 } name)
                {
                    costumeMap[entry.Name] = name;
                }
            }

            this._cache.Set(CostumeCacheKey, JsonSerializer.Serialize(costumeMap), CacheDuration);
            LogCachedCostumeEntries(this._logger, costumeMap.Count);
        }
        catch (Exception ex)
        {
            LogRefreshCostumeCacheFailed(this._logger, ex);
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Cached {Count} base stat entries.")]
    private static partial void LogCachedBaseStats(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Cached {Count} move entries.")]
    private static partial void LogCachedMoveEntries(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Cached {Count} item entries.")]
    private static partial void LogCachedItemEntries(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Cached {Count} costume entries.")]
    private static partial void LogCachedCostumeEntries(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to refresh master data cache.")]
    private static partial void LogRefreshCacheFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to refresh costume name cache; costume names will be unavailable.")]
    private static partial void LogRefreshCostumeCacheFailed(ILogger logger, Exception ex);
}
