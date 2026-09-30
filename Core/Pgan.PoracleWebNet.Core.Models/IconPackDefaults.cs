namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>
/// The server's copy of what the SPA's <c>IconService</c> falls back to, used to repair icon settings
/// that point at a repository which no longer exists.
/// </summary>
/// <remarks>
/// <c>DeadIconPackRepointTests</c> reads <c>icon.service.ts</c> and fails the build if the default pack or
/// any category's folder here stops matching it. See #877.
/// </remarks>
public static class IconPackDefaults
{
    /// <summary>The pack an unset category resolves under. <c>DEFAULT_UICONS</c> in icon.service.ts.</summary>
    public const string DefaultBase = "https://raw.githubusercontent.com/jms412/PkmnHomeIcons/master/UICONS";

    /// <summary>
    /// The repository every built-in default pointed into until #877. It is gone: the repository 404s,
    /// not just the path, so every icon under it renders as nothing.
    /// </summary>
    public const string DeadRepository = "raw.githubusercontent.com/whitewillem/PogoAssets";

    /// <summary>Each icon setting and the folder it lives in inside a UICONS pack. <c>SOURCES</c> in icon.service.ts.</summary>
    public static readonly IReadOnlyDictionary<string, string> FolderByKey = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["uicons_raid"] = "raid",
        ["uicons_gym"] = "gym",
        ["uicons_invasion"] = "invasion",
        ["uicons_pkmn"] = "pokemon",
        ["uicons_reward"] = "reward",
        ["uicons_type"] = "type",
    };

    /// <summary>True when <paramref name="value"/> is an http(s) URL inside the dead repository.</summary>
    public static bool PointsIntoDeadRepository(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        foreach (var scheme in new[] { "https://", "http://" })
        {
            if (trimmed.StartsWith(scheme + DeadRepository + "/", StringComparison.OrdinalIgnoreCase)
                || trimmed.Equals(scheme + DeadRepository, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
