namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>
/// Tracks which quick picks a user has applied, their exclusions, and the created alarm UIDs.
/// </summary>
public class QuickPickAppliedState
{
    /// <summary>
    /// The Discord/Telegram user ID that applied this quick pick.
    /// </summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// The profile number this quick pick was applied to.
    /// </summary>
    public int ProfileNo
    {
        get; set;
    }

    public string QuickPickId { get; set; } = string.Empty;
    public DateTime AppliedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Pokemon IDs that were excluded when this quick pick was applied.
    /// </summary>
    public List<int> ExcludePokemonIds { get; set; } = [];

    /// <summary>
    /// The UIDs of alarm rows created by this quick pick application.
    /// Used to identify and delete them on re-apply or removal.
    /// </summary>
    public List<int> TrackedUids { get; set; } = [];
}
