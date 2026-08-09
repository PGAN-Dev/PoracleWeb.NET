using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

public interface IQuickPickService
{
    public Task<IEnumerable<QuickPickSummary>> GetAllAsync(string userId, int profileNo);
    public Task<QuickPickDefinition?> GetByIdAsync(string id);

    /// <summary>Ownership-scoped read: global picks are public, user picks are visible only to their owner.</summary>
    public Task<QuickPickDefinition?> GetVisibleByIdAsync(string userId, string id);
    /// <summary>
    /// Saves a global quick pick. <paramref name="isSeeding"/> skips the ownership guard, which the
    /// seeding path shares and would otherwise trip over a user pick holding a built-in id. See #659.
    /// </summary>
    public Task<QuickPickDefinition> SaveAdminPickAsync(QuickPickDefinition definition, bool isSeeding = false);
    public Task<QuickPickDefinition> SaveUserPickAsync(string userId, QuickPickDefinition definition);
    public Task<bool> DeleteAdminPickAsync(string id);
    public Task<bool> DeleteUserPickAsync(string userId, string id);
    public Task<QuickPickAppliedState> ApplyAsync(string userId, int profileNo, string quickPickId, QuickPickApplyRequest request);
    public Task<QuickPickAppliedState> ReapplyAsync(string userId, int profileNo, string quickPickId, QuickPickApplyRequest request);
    public Task<bool> RemoveAsync(string userId, int profileNo, string quickPickId);
    public Task<IEnumerable<QuickPickDefinition>> GetDefaultPicksAsync();
    public Task SeedDefaultsAsync();
}
