using System.Collections.Concurrent;
using System.Text.Json;

namespace PGAN.Poracle.Web.Api.Services;

public class AvatarCacheService : BackgroundService
{
    private readonly ILogger<AvatarCacheService> _logger;

    private static readonly ConcurrentDictionary<string, string> Avatars = new();
    private static readonly string CacheFile = Path.Combine(
        Environment.GetEnvironmentVariable("DATA_DIR") ?? Directory.GetCurrentDirectory(),
        "avatar-cache.json");

    public AvatarCacheService(ILogger<AvatarCacheService> logger)
    {
        _logger = logger;
    }

    public static string? GetAvatar(string userId) =>
        Avatars.TryGetValue(userId, out var url) ? url : null;

    /// <summary>Store an avatar URL (e.g. captured at login time).</summary>
    public static void SetAvatar(string userId, string url)
    {
        Avatars[userId] = url;
    }

    /// <summary>Save the current cache to disk.</summary>
    public static void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(Avatars.ToDictionary(x => x.Key, x => x.Value));
            File.WriteAllText(CacheFile, json);
        }
        catch { /* best effort */ }
    }

    protected override Task ExecuteAsync(CancellationToken ct)
    {
        LoadFromDisk();
        _logger.LogInformation("Loaded {Count} avatars from disk cache.", Avatars.Count);
        return Task.CompletedTask;
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
}
