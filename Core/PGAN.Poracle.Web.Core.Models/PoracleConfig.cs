using System.Text.Json.Serialization;

namespace PGAN.Poracle.Web.Core.Models;

public class PoracleConfig
{
    public string Locale { get; set; } = string.Empty;

    [JsonPropertyName("providerURL")]
    public string ProviderUrl { get; set; } = string.Empty;

    public string StaticKey { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string PoracleVersion { get; set; } = string.Empty;

    public int PvpFilterMaxRank { get; set; }
    public int PvpFilterLittleMinCp { get; set; }
    public int PvpFilterGreatMinCp { get; set; }
    public int PvpFilterUltraMinCp { get; set; }
    public bool PvpLittleLeagueAllowed { get; set; }
    public string DefaultTemplateName { get; set; } = string.Empty;
    public string EverythingFlagPermissions { get; set; } = string.Empty;
    public int MaxDistance { get; set; }
    public PoracleAdmins? Admins { get; set; }
}

public class PoracleAdmins
{
    public List<string> Discord { get; set; } = new();
    public List<string> Telegram { get; set; } = new();
}
