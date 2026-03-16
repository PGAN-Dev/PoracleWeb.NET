using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PGAN.Poracle.Web.Api.Configuration;
using PGAN.Poracle.Web.Core.Abstractions.Services;

namespace PGAN.Poracle.Web.Api.Services;

public class AvatarCacheService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly DiscordSettings _discord;
    private readonly ILogger<AvatarCacheService> _logger;

    private static readonly ConcurrentDictionary<string, string> Avatars = new();
    private static bool _initialLoadComplete;
    private static readonly string CacheFile = Path.Combine(
        Environment.GetEnvironmentVariable("DATA_DIR") ?? Directory.GetCurrentDirectory(),
        "avatar-cache.json");

    public AvatarCacheService(
        IServiceProvider services,
        IOptions<DiscordSettings> discord,
        ILogger<AvatarCacheService> logger)
    {
        _services = services;
        _discord = discord.Value;
        _logger = logger;
    }

    public static string? GetAvatar(string userId) =>
        Avatars.TryGetValue(userId, out var url) ? url : null;

    public static bool IsReady => _initialLoadComplete;

    /// <summary>Fetch a single user's avatar on demand (for new users).</summary>
    public static async Task<string?> FetchSingleAsync(string userId, string botToken)
    {
        if (Avatars.TryGetValue(userId, out var existing)) return existing;
        if (string.IsNullOrEmpty(botToken)) return null;

        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Authorization", $"Bot {botToken}");
            client.DefaultRequestHeaders.Add("User-Agent", "DiscordBot (https://pgan.me, 1.0)");
            client.Timeout = TimeSpan.FromSeconds(5);

            var resp = await client.GetAsync($"https://discordapp.com/api/v9/users/{userId}");
            if (!resp.IsSuccessStatusCode) return null;

            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("avatar", out var av) && av.ValueKind == JsonValueKind.String)
            {
                var hash = av.GetString();
                if (!string.IsNullOrEmpty(hash))
                {
                    var ext = hash.StartsWith("a_") ? "gif" : "png";
                    var url = $"https://cdn.discordapp.com/avatars/{userId}/{hash}.{ext}";
                    Avatars[userId] = url;
                    return url;
                }
            }
        }
        catch { }
        return null;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_discord.BotToken))
        {
            _logger.LogInformation("No Discord bot token, avatar cache disabled.");
            _initialLoadComplete = true;
            return;
        }

        // Load from disk cache immediately
        LoadFromDisk();
        _initialLoadComplete = true;
        _logger.LogInformation("Loaded {Count} avatars from disk cache.", Avatars.Count);

        // Wait for app startup
        await Task.Delay(5000, ct);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RefreshFromDiscordAsync(ct);
                SaveToDisk();
            }
            catch (TaskCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Avatar refresh failed");
            }

            // Run once per day
            await Task.Delay(TimeSpan.FromHours(24), ct);
        }
    }

    private async Task RefreshFromDiscordAsync(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var humanService = scope.ServiceProvider.GetRequiredService<IHumanService>();
        var humans = await humanService.GetAllAsync();

        var discordIds = humans
            .Where(h => h.Type?.StartsWith("discord") == true)
            .Select(h => h.Id)
            .Where(id => !Avatars.ContainsKey(id))
            .ToList();

        if (discordIds.Count == 0)
        {
            _logger.LogInformation("All avatars cached ({Total} total), nothing to fetch.", Avatars.Count);
            return;
        }

        _logger.LogInformation("Fetching {Count} new Discord avatars...", discordIds.Count);

        using var client = new HttpClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bot {_discord.BotToken}");
        client.DefaultRequestHeaders.Add("User-Agent", "DiscordBot (https://pgan.me, 1.0)");
        client.Timeout = TimeSpan.FromSeconds(10);

        var fetched = 0;

        foreach (var userId in discordIds)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                var resp = await client.GetAsync($"https://discordapp.com/api/v9/users/{userId}", ct);

                if (resp.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    var wait = 2.0;
                    if (resp.Headers.TryGetValues("Retry-After", out var vals))
                        double.TryParse(vals.FirstOrDefault(), out wait);
                    await Task.Delay(TimeSpan.FromSeconds(Math.Max(wait, 1)), ct);
                    resp = await client.GetAsync($"https://discordapp.com/api/v9/users/{userId}", ct);
                }

                if (resp.IsSuccessStatusCode)
                {
                    var json = await resp.Content.ReadAsStringAsync(ct);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("avatar", out var av) && av.ValueKind == JsonValueKind.String)
                    {
                        var hash = av.GetString();
                        if (!string.IsNullOrEmpty(hash))
                        {
                            var ext = hash.StartsWith("a_") ? "gif" : "png";
                            Avatars[userId] = $"https://cdn.discordapp.com/avatars/{userId}/{hash}.{ext}";
                        }
                    }
                    fetched++;
                }

                await Task.Delay(100, ct);

                // Save periodically
                if (fetched % 100 == 0 && fetched > 0)
                {
                    _logger.LogInformation("Avatar progress: {Fetched}/{Total}", fetched, discordIds.Count);
                    SaveToDisk();
                }
            }
            catch (TaskCanceledException) { break; }
            catch { }
        }

        _logger.LogInformation("Avatar fetch done: {Fetched} new, {Total} total.", fetched, Avatars.Count);
    }

    private void LoadFromDisk()
    {
        try
        {
            if (!File.Exists(CacheFile)) return;
            var json = File.ReadAllText(CacheFile);
            var data = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (data == null) return;
            foreach (var (k, v) in data) Avatars.TryAdd(k, v);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load avatar cache from disk");
        }
    }

    private void SaveToDisk()
    {
        try
        {
            var json = JsonSerializer.Serialize(Avatars.ToDictionary(x => x.Key, x => x.Value));
            File.WriteAllText(CacheFile, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save avatar cache to disk");
        }
    }
}
