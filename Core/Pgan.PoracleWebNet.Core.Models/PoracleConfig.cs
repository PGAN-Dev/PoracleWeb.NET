using System.Text.Json.Serialization;

namespace Pgan.PoracleWebNet.Core.Models;

public class PoracleConfig
{
    public string Locale { get; set; } = string.Empty;

    [JsonPropertyName("providerURL")]
    public string ProviderUrl { get; set; } = string.Empty;

    public string StaticKey { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string PoracleVersion { get; set; } = string.Empty;

    public int PvpFilterMaxRank
    {
        get; set;
    }
    public int PvpFilterLittleMinCp
    {
        get; set;
    }
    public int PvpFilterGreatMinCp
    {
        get; set;
    }
    public int PvpFilterUltraMinCp
    {
        get; set;
    }
    public bool PvpLittleLeagueAllowed
    {
        get; set;
    }

    /// <summary>
    /// PvP level caps offered by Poracle (e.g. [50] or [50, 51]).
    /// Sourced from Poracle's <c>pvp.levelCaps</c> config and exposed via <c>/api/config/poracleWeb</c>.
    /// </summary>
    public List<int> PvpCaps { get; set; } = [];

    /// <summary>
    /// Default cap pre-selected when a user creates a new PvP-tracked monster alarm.
    /// <c>0</c> = match all caps. Sourced from Poracle's <c>tracking.defaultUserTrackingLevelCap</c>.
    /// </summary>
    public int DefaultPvpCap
    {
        get; set;
    }

    public string DefaultTemplateName { get; set; } = string.Empty;
    public string EverythingFlagPermissions { get; set; } = string.Empty;
    public int MaxDistance
    {
        get; set;
    }
    public PoracleAdmins? Admins
    {
        get; set;
    }
    public List<PoracleDelegateEntry> DelegateAdministration { get; set; } = [];
}

public class PoracleAdmins
{
    public List<string> Discord { get; set; } = [];
    public List<string> Telegram { get; set; } = [];
}

public class PoracleDelegateEntry
{
    /// <summary>Webhook URL (matches the `id` column in humans table).</summary>
    public string WebhookId { get; set; } = string.Empty;
    public List<string> DiscordIds { get; set; } = [];
}
