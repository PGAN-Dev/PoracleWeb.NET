using System.ComponentModel.DataAnnotations;
namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>
/// A reusable alarm preset that can be applied to create tracking entries.
/// </summary>
public class QuickPickDefinition
{
    // Every one of these is bounded in the database (QuickPickDefinitionConfiguration) and was bounded
    // nowhere else, so an over-long value reached MySQL and came back as an opaque 500. Neither quick-
    // pick dialog sets a maxlength, so naming a preset with a sentence was enough. See #549.
    [StringLength(50, ErrorMessage = "id must be 50 characters or fewer.")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [Required(AllowEmptyStrings = false, ErrorMessage = "A name is required.")]
    [StringLength(200, ErrorMessage = "name must be 200 characters or fewer.")]
    public string Name { get; set; } = string.Empty;
    // TEXT, so 65535. The only sibling #549 missed, and the only one that still answered 500.
    [StringLength(65535, ErrorMessage = "description must be 65535 characters or fewer.")]
    public string Description { get; set; } = string.Empty;
    [StringLength(50, ErrorMessage = "icon must be 50 characters or fewer.")]
    public string Icon { get; set; } = "bolt";
    [StringLength(50, ErrorMessage = "category must be 50 characters or fewer.")]
    public string Category { get; set; } = "Common";
    [StringLength(20, ErrorMessage = "alarmType must be 20 characters or fewer.")]
    public string AlarmType { get; set; } = "monster"; // monster, raid, egg, quest, invasion, lure, nest, gym, maxbattle
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
    /// The Discord/Telegram user ID that owns this definition. Null for global (admin) picks.
    /// </summary>
    public string? OwnerUserId
    {
        get; set;
    }

    /// <summary>
    /// The alarm filter parameters as a flexible dictionary.
    /// Keys match the alarm model properties (e.g., minIv, maxIv, pokemonId, level, etc.).
    /// </summary>
    public Dictionary<string, object?> Filters { get; set; } = [];
}
