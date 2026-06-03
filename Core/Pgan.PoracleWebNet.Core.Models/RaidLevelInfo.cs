namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>
/// Canonical raid-level metadata served to the frontend so the level selector
/// and alarm cards can render the masterfile vocabulary.
///
/// Source of truth: WatWowMap masterfile (raid_{N} / raid_{N}_plural keys).
/// </summary>
public class RaidLevelInfo
{
    /// <summary>Backend integer matched against PoracleNG webhook level. 1-19 currently named.</summary>
    public int Value
    {
        get; set;
    }

    /// <summary>Coarse grouping: star, mega, special, shadow, superMega, coordinated.</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>Singular English name from the masterfile, e.g. "1 Star Raid", "Mega Legendary Raid".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Plural English name from the masterfile, e.g. "1 Star Raids".</summary>
    public string NamePlural { get; set; } = string.Empty;
}
