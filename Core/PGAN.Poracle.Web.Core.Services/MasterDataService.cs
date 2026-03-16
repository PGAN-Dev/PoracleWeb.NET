using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using PGAN.Poracle.Web.Core.Abstractions.Services;

namespace PGAN.Poracle.Web.Core.Services;

public class MasterDataService : IMasterDataService
{
    private readonly IMemoryCache _cache;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<MasterDataService> _logger;

    private const string PokemonCacheKey = "MasterData_Pokemon";
    private const string ItemCacheKey = "MasterData_Items";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(24);

    private const string MasterfileUrl =
        "https://raw.githubusercontent.com/WatWowMap/Masterfile-Generator/master/master-latest-poracle.json";

    private bool _initialized;

    public MasterDataService(
        IMemoryCache cache,
        IHttpClientFactory httpClientFactory,
        ILogger<MasterDataService> logger)
    {
        _cache = cache;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<string?> GetPokemonDataAsync()
    {
        await EnsureInitializedAsync();
        _cache.TryGetValue(PokemonCacheKey, out string? data);
        return data;
    }

    public async Task<string?> GetItemDataAsync()
    {
        await EnsureInitializedAsync();
        _cache.TryGetValue(ItemCacheKey, out string? data);
        return data;
    }

    public async Task RefreshCacheAsync()
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("PGAN-PoracleWeb/1.0");
            client.Timeout = TimeSpan.FromSeconds(30);

            _logger.LogInformation("Fetching Pokemon masterdata from GitHub...");
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
            _cache.Set(PokemonCacheKey, JsonSerializer.Serialize(pokemonMap), CacheDuration);
            _logger.LogInformation("Cached {Count} pokemon entries.", pokemonMap.Count);

            // Build item name map
            var itemMap = new Dictionary<string, string>();
            if (root.TryGetProperty("items", out var items))
            {
                foreach (var entry in items.EnumerateObject())
                {
                    var id = entry.Name;
                    var name = id;
                    if (entry.Value.TryGetProperty("name", out var nameProp))
                        name = nameProp.GetString() ?? id;
                    else if (entry.Value.ValueKind == JsonValueKind.String)
                        name = entry.Value.GetString() ?? id;
                    itemMap[id] = name;
                }
            }
            _cache.Set(ItemCacheKey, JsonSerializer.Serialize(itemMap), CacheDuration);
            _logger.LogInformation("Cached {Count} item entries.", itemMap.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh master data cache.");
        }
    }

    private async Task EnsureInitializedAsync()
    {
        if (_initialized) return;
        _initialized = true;
        await RefreshCacheAsync();
    }
}
