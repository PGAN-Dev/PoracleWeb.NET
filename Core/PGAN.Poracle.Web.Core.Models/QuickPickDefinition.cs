namespace PGAN.Poracle.Web.Core.Models;

/// <summary>
/// A reusable alarm preset that can be applied to create tracking entries.
/// Stored as JSON in pweb_settings.
/// </summary>
public class QuickPickDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Icon { get; set; } = "bolt";
    public string Category { get; set; } = "Common";
    public string AlarmType { get; set; } = "monster"; // monster, raid, egg, quest, invasion, lure, nest, gym
    public int SortOrder
    {
        get; set;
    }
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Scope: "global" for admin-defined, "user" for user-defined.
    /// </summary>
    public string Scope { get; set; } = "global";

    /// <summary>
    /// The alarm filter parameters as a flexible dictionary.
    /// Keys match the alarm model properties (e.g., minIv, maxIv, pokemonId, level, etc.).
    /// </summary>
    public Dictionary<string, object?> Filters { get; set; } = [];
}
