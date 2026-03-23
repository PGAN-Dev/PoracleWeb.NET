using Microsoft.Extensions.Logging;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Abstractions.Services;

namespace Pgan.PoracleWebNet.Core.Services;

public partial class WebhookDelegateService(
    IWebhookDelegateRepository repository,
    ILogger<WebhookDelegateService> logger) : IWebhookDelegateService
{
    private readonly IWebhookDelegateRepository _repository = repository;
    private readonly ILogger<WebhookDelegateService> _logger = logger;

    public async Task<Dictionary<string, string[]>> GetAllGroupedAsync()
    {
        var all = await this._repository.GetAllAsync();
        return all
            .GroupBy(d => d.WebhookId)
            .ToDictionary(g => g.Key, g => g.Select(d => d.UserId).ToArray());
    }

    public async Task<string[]> GetDelegatesForWebhookAsync(string webhookId)
    {
        var delegates = await this._repository.GetByWebhookIdAsync(webhookId);
        return [.. delegates.Select(d => d.UserId)];
    }

    public async Task<IEnumerable<string>> GetManagedWebhookIdsAsync(string userId) =>
        await this._repository.GetWebhookIdsByUserIdAsync(userId);

    public async Task<string[]> AddDelegateAsync(string webhookId, string userId)
    {
        await this._repository.AddAsync(webhookId, userId);
        LogDelegateAdded(this._logger, userId, webhookId);
        return await this.GetDelegatesForWebhookAsync(webhookId);
    }

    public async Task<string[]> RemoveDelegateAsync(string webhookId, string userId)
    {
        await this._repository.RemoveAsync(webhookId, userId);
        LogDelegateRemoved(this._logger, userId, webhookId);
        return await this.GetDelegatesForWebhookAsync(webhookId);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Added delegate user '{UserId}' to webhook '{WebhookId}'")]
    private static partial void LogDelegateAdded(ILogger logger, string userId, string webhookId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Removed delegate user '{UserId}' from webhook '{WebhookId}'")]
    private static partial void LogDelegateRemoved(ILogger logger, string userId, string webhookId);
}
