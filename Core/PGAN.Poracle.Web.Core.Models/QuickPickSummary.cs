namespace PGAN.Poracle.Web.Core.Models;

/// <summary>
/// Combined view returned to the frontend: the quick pick definition plus its applied state for the current user.
/// </summary>
public class QuickPickSummary
{
    public QuickPickDefinition Definition { get; set; } = null!;

    /// <summary>
    /// Null if not applied by the current user on the current profile.
    /// </summary>
    public QuickPickAppliedState? AppliedState
    {
        get; set;
    }
}
