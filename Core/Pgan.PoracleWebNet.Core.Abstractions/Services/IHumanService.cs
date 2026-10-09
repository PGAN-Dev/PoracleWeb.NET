using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

public interface IHumanService
{
    public Task<IEnumerable<Human>> GetAllAsync();

    /// <summary>The webhook humans, id and name. Used to resolve a delegated webhook named by name.</summary>
    public Task<IEnumerable<Human>> GetWebhooksAsync();

    /// <summary>A batch of humans by id, skipping any id that does not exist. Used to resolve owner and
    /// reviewer names for a list of geofences in one round trip rather than one per name.</summary>
    public Task<IEnumerable<Human>> GetByIdsAsync(IEnumerable<string> ids);
    public Task<Human?> GetByIdAsync(string id);
    public Task<Human> CreateAsync(Human human);

    /// <summary>Sets the language PoracleNG writes this user's alerts in.</summary>
    public Task SetLanguageAsync(string userId, string language);

    public Task<bool> ExistsAsync(string id);
    public Task<int> DeleteAllAlarmsByUserAsync(string userId);
    public Task<bool> DeleteUserAsync(string userId);
}
