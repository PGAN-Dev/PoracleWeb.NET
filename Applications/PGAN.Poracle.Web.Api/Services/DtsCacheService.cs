using System.Text.Json;

namespace PGAN.Poracle.Web.Api.Services;

/// <summary>
/// Background service that loads DTS (Discord Template System) files from
/// the Poracle config directory and caches them for the template preview API.
///
/// DTS files are read from:
///   1. DTS_SOURCE_DIR env var (Docker volume mount of PoracleJS config)
///   2. Fallback: DATA_DIR/dts-cache.json (manually placed file)
///
/// The service watches for file changes and reloads automatically.
/// </summary>
public class DtsCacheService(ILogger<DtsCacheService> logger) : BackgroundService
{
    private readonly ILogger<DtsCacheService> _logger = logger;
    private static string? _cachedJson;
    private static readonly Lock Lock = new();

    public static string? GetCachedDts() => _cachedJson;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(2000, stoppingToken);
        this.LoadDts();

        // Watch for changes and reload every hour
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            this.LoadDts();
        }
    }

    private void LoadDts()
    {
        try
        {
            var sourceDir = Environment.GetEnvironmentVariable("DTS_SOURCE_DIR");
            var dataDir = Environment.GetEnvironmentVariable("DATA_DIR") ?? Directory.GetCurrentDirectory();

            // Strategy 1: Read from Poracle config directory (Docker volume)
            if (!string.IsNullOrEmpty(sourceDir) && Directory.Exists(sourceDir))
            {
                var merged = this.LoadFromPoracleConfig(sourceDir);
                if (merged != null)
                {
                    lock (Lock)
                    {
                        _cachedJson = merged;
                    }
                    // Also save to data dir as a cache
                    var cachePath = Path.Combine(dataDir, "dts-cache.json");
                    File.WriteAllText(cachePath, merged);
                    this._logger.LogInformation("DTS loaded from Poracle config at {Dir}", sourceDir);
                    return;
                }
            }

            // Strategy 2: Read from cached file in data directory
            var fallbackPath = Path.Combine(dataDir, "dts-cache.json");
            if (File.Exists(fallbackPath))
            {
                var json = File.ReadAllText(fallbackPath);
                lock (Lock)
                {
                    _cachedJson = json;
                }
                this._logger.LogInformation("DTS loaded from cache file at {Path}", fallbackPath);
                return;
            }

            this._logger.LogWarning("No DTS files found. Template previews will be unavailable.");
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Failed to load DTS files");
        }
    }

    private string? LoadFromPoracleConfig(string configDir)
    {
        try
        {
            var jsonOptions = new JsonSerializerOptions
            {
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            };

            // Look for DTS files in order of priority
            var candidates = new[]
            {
                Path.Combine(configDir, "dts.json"),                    // Main config
                Path.Combine(configDir, "config", "dts.json"),          // config/dts.json
                Path.Combine(configDir, "defaults", "dts.json"),        // defaults/dts.json
                Path.Combine(configDir, "config", "defaults", "dts.json"),
            };

            var mainDtsPath = candidates.FirstOrDefault(File.Exists);
            if (mainDtsPath == null)
            {
                return null;
            }

            var mainJson = File.ReadAllText(mainDtsPath);
            var mainEntries = JsonSerializer.Deserialize<JsonElement[]>(mainJson, jsonOptions);
            if (mainEntries == null)
            {
                return null;
            }

            this._logger.LogInformation("Loaded {Count} DTS entries from {Path}", mainEntries.Length, mainDtsPath);

            // Look for override files
            var overrideCandidates = new[]
            {
                Path.Combine(configDir, "dts", "dts.json"),
                Path.Combine(configDir, "config", "dts", "dts.json"),
            };

            var overridePath = overrideCandidates.FirstOrDefault(File.Exists);
            if (overridePath != null)
            {
                try
                {
                    var overrideJson = File.ReadAllText(overridePath);
                    var overrides = JsonSerializer.Deserialize<JsonElement[]>(overrideJson, jsonOptions);
                    if (overrides != null && overrides.Length > 0)
                    {
                        // Merge overrides into main (replace matching id+type+platform)
                        var merged = new List<JsonElement>(mainEntries);
                        foreach (var o in overrides)
                        {
                            var oId = o.TryGetProperty("id", out var idP) ? idP.GetRawText() : "";
                            var oType = o.TryGetProperty("type", out var typeP) ? typeP.GetString() : "";
                            var oPlatform = o.TryGetProperty("platform", out var platP) ? platP.GetString() : "";

                            var idx = merged.FindIndex(m =>
                            {
                                var mId = m.TryGetProperty("id", out var mi) ? mi.GetRawText() : "";
                                var mType = m.TryGetProperty("type", out var mt) ? mt.GetString() : "";
                                var mPlatform = m.TryGetProperty("platform", out var mp) ? mp.GetString() : "";
                                return mId == oId && mType == oType && mPlatform == oPlatform;
                            });

                            if (idx >= 0)
                            {
                                merged[idx] = o;
                            }
                            else
                            {
                                merged.Add(o);
                            }
                        }

                        this._logger.LogInformation("Merged {Count} DTS overrides from {Path}", overrides.Length, overridePath);
                        return JsonSerializer.Serialize(merged);
                    }
                }
                catch (Exception ex)
                {
                    this._logger.LogWarning(ex, "Failed to parse DTS override file at {Path}", overridePath);
                }
            }

            return mainJson;
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Failed to parse DTS from Poracle config at {Dir}", configDir);
            return null;
        }
    }
}
