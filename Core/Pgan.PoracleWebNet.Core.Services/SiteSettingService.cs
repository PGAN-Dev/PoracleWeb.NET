using Microsoft.Extensions.Logging;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

public partial class SiteSettingService(
    ISiteSettingRepository repository,
    ILogger<SiteSettingService> logger) : ISiteSettingService
{
    private readonly ISiteSettingRepository _repository = repository;
    private readonly ILogger<SiteSettingService> _logger = logger;

    /// <summary>
    /// Keys that are safe to expose via the unauthenticated public endpoint.
    /// Expand this set as new public-facing settings are added.
    /// </summary>
    private static readonly HashSet<string> PublicKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "custom_title",
    };

    public async Task<IEnumerable<SiteSetting>> GetAllAsync() => await this._repository.GetAllAsync();

    public async Task<IEnumerable<SiteSetting>> GetByCategoryAsync(string category) =>
        await this._repository.GetByCategoryAsync(category);

    public async Task<IEnumerable<SiteSetting>> GetPublicAsync()
    {
        var results = new List<SiteSetting>();
        foreach (var key in PublicKeys)
        {
            var setting = await this._repository.GetByKeyAsync(key);
            if (setting is not null)
            {
                results.Add(setting);
            }
        }

        return results;
    }

    public async Task<SiteSetting?> GetByKeyAsync(string key) => await this._repository.GetByKeyAsync(key);

    public async Task<string?> GetValueAsync(string key)
    {
        var setting = await this._repository.GetByKeyAsync(key);
        return setting?.Value;
    }

    public async Task<bool> GetBoolAsync(string key)
    {
        var value = await this.GetValueAsync(key);
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<SiteSetting> CreateOrUpdateAsync(SiteSetting setting)
    {
        var result = await this._repository.CreateOrUpdateAsync(setting);
        LogSettingUpdated(this._logger, setting.Key, setting.Category);
        return result;
    }

    public async Task<bool> DeleteAsync(string key)
    {
        var result = await this._repository.DeleteAsync(key);
        if (result)
        {
            LogSettingDeleted(this._logger, key);
        }

        return result;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Site setting '{Key}' updated (category: {Category})")]
    private static partial void LogSettingUpdated(ILogger logger, string key, string category);

    [LoggerMessage(Level = LogLevel.Information, Message = "Site setting '{Key}' deleted")]
    private static partial void LogSettingDeleted(ILogger logger, string key);
}
