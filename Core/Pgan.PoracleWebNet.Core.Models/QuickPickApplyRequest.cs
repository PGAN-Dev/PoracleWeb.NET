using System.ComponentModel.DataAnnotations;

namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>
/// Request body for applying a quick pick with optional exclusions and delivery overrides.
/// </summary>
public class QuickPickApplyRequest
{
    /// <summary>
    /// Pokemon IDs to exclude when applying (for monster-type picks).
    /// </summary>
    public List<int> ExcludePokemonIds { get; set; } = [];

    /// <summary>
    /// Saved place the created alarms measure their radius from. Empty means the profile pin.
    /// </summary>
    /// <remarks>
    /// A quick pick creates ordinary alarms, so it can carry the same delivery scope any other alarm
    /// can. Without these two the apply dialog could offer a scope it had no way to send. See #730.
    /// </remarks>
    [StringLength(64)]
    public string? OverrideLocationLabel
    {
        get; set;
    }

    /// <summary>Areas the created alarms are confined to. Mutually exclusive with a distance.</summary>
    [MaxLength(32)]
    public List<string>? OverrideAreas
    {
        get; set;
    }

    /// <summary>
    /// Override distance (in meters). Null = use default (0 = areas mode).
    /// </summary>
    public int? Distance
    {
        get; set;
    }

    /// <summary>
    /// Override clean flag. Null = use default.
    /// </summary>
    public int? Clean
    {
        get; set;
    }

    /// <summary>
    /// Override template name. Null = use default.
    /// </summary>
    public string? Template
    {
        get; set;
    }
}
