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
