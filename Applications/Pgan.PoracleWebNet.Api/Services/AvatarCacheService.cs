using System.Collections.Concurrent;
using System.Text.Json;

namespace Pgan.PoracleWebNet.Api.Services;

public partial class AvatarCacheService(ILogger<AvatarCacheService> logger) : BackgroundService
{
    private readonly ILogger<AvatarCacheService> _logger = logger;

    private static readonly ConcurrentDictionary<string, string> Avatars = new();
    private static readonly string CacheFile = Path.Combine(
        Environment.GetEnvironmentVariable("DATA_DIR") ?? Directory.GetCurrentDirectory(),
        "avatar-cache.json");

    public static string? GetAvatar(string userId) =>
        Avatars.TryGetValue(userId, out var url) ? url : null;

    /// <summary>Store an avatar URL (e.g. captured at login time).</summary>
    public static void SetAvatar(string userId, string url) => Avatars[userId] = url;

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

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        this.LoadFromDisk();
        LogLoadedAvatars(this._logger, Avatars.Count);
        return Task.CompletedTask;
    }

    private void LoadFromDisk()
    {
        try
        {
            if (!File.Exists(CacheFile))
            {
                return;
            }

            var json = File.ReadAllText(CacheFile);
            var data = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (data == null)
            {
                return;
            }

            foreach (var (k, v) in data)
            {
                Avatars.TryAdd(k, v);
            }
        }
        catch (Exception ex)
        {
            LogAvatarCacheLoadFailed(this._logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Loaded {Count} avatars from disk cache.")]
    private static partial void LogLoadedAvatars(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to load avatar cache from disk")]
    private static partial void LogAvatarCacheLoadFailed(ILogger logger, Exception ex);
}
