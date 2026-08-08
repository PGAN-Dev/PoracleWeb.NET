using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Abstractions.Repositories;

public interface IQuickPickAppliedStateRepository
{
    public Task<QuickPickAppliedState?> GetAsync(string userId, int profileNo, string quickPickId);
    public Task<List<QuickPickAppliedState>> GetByUserAndProfileAsync(string userId, int profileNo);

    /// <summary>
    /// Every applied state for the user across all profiles. Used by the uid remapper, which cannot know
    /// which profile an edited alarm belongs to.
    /// </summary>
    public Task<List<QuickPickAppliedState>> GetByUserAsync(string userId);
    public Task CreateOrUpdateAsync(QuickPickAppliedState state);
    public Task DeleteAsync(string userId, int profileNo, string quickPickId);

    /// <summary>
    /// Removes every applied-state row for a quick pick, optionally narrowed to one user.
    /// </summary>
    /// <remarks>
    /// Deleting a definition left its applied state behind. Nothing could reach it -- the listing walks
    /// definitions -- so it leaked, and re-creating a pick under the same id resurrected a stale "applied"
    /// badge pointing at alarm uids from the old one. See #470.
    /// </remarks>
    public Task DeleteByQuickPickIdAsync(string quickPickId, string? userId = null);
}
