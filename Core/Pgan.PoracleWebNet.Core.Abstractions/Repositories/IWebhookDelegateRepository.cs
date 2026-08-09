using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Abstractions.Repositories;

public interface IWebhookDelegateRepository
{
    public Task<IEnumerable<WebhookDelegate>> GetAllAsync();
    public Task<IEnumerable<WebhookDelegate>> GetByWebhookIdAsync(string webhookId);
    public Task<IEnumerable<string>> GetWebhookIdsByUserIdAsync(string userId);
    public Task<WebhookDelegate> AddAsync(string webhookId, string userId);
    public Task<bool> RemoveAsync(string webhookId, string userId);
    public Task<bool> RemoveAllForWebhookAsync(string webhookId);

    /// <summary>Removes every grant naming this id, as the webhook or as the delegate. See #512.</summary>
    public Task<int> RemoveAllForIdAsync(string id);
}
