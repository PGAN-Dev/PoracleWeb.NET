using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Core.Abstractions.Services;

public interface IQuickPickService
{
    public Task<IEnumerable<QuickPickSummary>> GetAllAsync(string userId, int profileNo);
    public Task<QuickPickDefinition?> GetByIdAsync(string id);
    public Task<QuickPickDefinition> SaveAdminPickAsync(QuickPickDefinition definition);
    public Task<QuickPickDefinition> SaveUserPickAsync(string userId, QuickPickDefinition definition);
    public Task<bool> DeleteAdminPickAsync(string id);
    public Task<bool> DeleteUserPickAsync(string userId, string id);
    public Task<QuickPickAppliedState> ApplyAsync(string userId, int profileNo, string quickPickId, QuickPickApplyRequest request);
    public Task<QuickPickAppliedState> ReapplyAsync(string userId, int profileNo, string quickPickId, QuickPickApplyRequest request);
    public Task<bool> RemoveAsync(string userId, int profileNo, string quickPickId);
    public Task<IEnumerable<QuickPickDefinition>> GetDefaultPicksAsync();
    public Task SeedDefaultsAsync();
}
