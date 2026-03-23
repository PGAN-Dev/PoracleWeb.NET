namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

public interface IWebhookDelegateService
{
    public Task<Dictionary<string, string[]>> GetAllGroupedAsync();
    public Task<string[]> GetDelegatesForWebhookAsync(string webhookId);
    public Task<IEnumerable<string>> GetManagedWebhookIdsAsync(string userId);
    public Task<string[]> AddDelegateAsync(string webhookId, string userId);
    public Task<string[]> RemoveDelegateAsync(string webhookId, string userId);
}
