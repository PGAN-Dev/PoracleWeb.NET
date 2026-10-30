using System.Text.Json.Serialization;

namespace Pgan.PoracleWebNet.Core.Models;

public class PoracleConfig
{
    public string Locale { get; set; } = string.Empty;

    /// <summary>
    /// The prefix the Poracle bot's commands take, e.g. <c>$!</c> or <c>!</c>. Needed because the only
    /// way a user can clear the bot's DMs is to send it <c>{prefix}poracle-clean</c>, and telling them
    /// the wrong prefix is worse than telling them nothing.
    /// </summary>
    public string Prefix { get; set; } = string.Empty;

    [JsonPropertyName("providerURL")]
    public string ProviderUrl { get; set; } = string.Empty;

    /// <summary>
    /// PoracleNG's <c>[geocoding] provider</c>, verbatim: <c>nominatim</c>, <c>photon</c>, <c>google</c>,
    /// or empty or <c>none</c> when it has no geocoder, in which case every address search and reverse
    /// lookup answers 503.
    /// </summary>
    /// <remarks>
    /// Null when the field is absent (PoracleJS sends none), which is no opinion and must not be read as
    /// "no geocoder". See #1111.
    /// </remarks>
    public string? Provider
    {
        get; set;
    }

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
    /// <summary>
    /// Webhook types the upstream Poracle deployment has switched off, as reported by
    /// <c>GET /api/config/poracleWeb</c> (e.g. <c>["raid", "quest"]</c>).
    /// </summary>
    /// <remarks>
    /// <c>null</c> means the field was absent — PoracleJS, or a Poracle that could not be read, which has no opinion —
    /// and is deliberately distinct from an empty list, which means "nothing is disabled upstream".
    /// Only the latter is safe to enforce on. Translate to <c>disable_*</c> keys with
    /// <see cref="PoracleDisabledHookMap.ToDisableKeys"/>. Fort changes appear here as <c>fort</c> from
    /// every supported PoracleNG (5.4.0 and later).
    /// </remarks>
    public List<string>? DisabledHooks
    {
        get; set;
    }

    /// <summary>
    /// The exact set of language codes Poracle will accept for a human's alert language, as reported by
    /// <c>availableLanguages</c> on <c>GET /api/config/poracleWeb</c>.
    /// </summary>
    /// <remarks>
    /// <c>null</c> means unrestricted — any code is accepted. Upstream reports null for an unset and an
    /// empty map alike, because its own write path only validates a non-empty one, so the two are
    /// genuinely the same answer. Added in PoracleNG 5.2.1 (jfberry/PoracleNG#197); an older server sends
    /// no field, which reads as null too.
    /// </remarks>
    public List<string>? AvailableLanguages
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
