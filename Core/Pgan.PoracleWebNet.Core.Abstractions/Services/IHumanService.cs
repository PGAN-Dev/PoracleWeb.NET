using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

public interface IHumanService
{
    public Task<IEnumerable<Human>> GetAllAsync();

    /// <summary>The webhook humans, id and name. Used to resolve a delegated webhook named by name.</summary>
    public Task<IEnumerable<Human>> GetWebhooksAsync();
    public Task<Human?> GetByIdAsync(string id);
    public Task<Human> CreateAsync(Human human);
    public Task<Human> UpdateAsync(Human human);
    public Task<bool> ExistsAsync(string id);
    public Task<int> DeleteAllAlarmsByUserAsync(string userId);
    public Task<bool> DeleteUserAsync(string userId);
}
