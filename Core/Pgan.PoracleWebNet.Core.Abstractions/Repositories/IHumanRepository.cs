using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Abstractions.Repositories;

public interface IHumanRepository
{
    public Task<IEnumerable<Human>> GetAllAsync();

    /// <summary>The webhook humans, id and name. Used to resolve a delegated webhook named by name.</summary>
    public Task<IEnumerable<Human>> GetWebhooksAsync();
    public Task<IEnumerable<Human>> GetByIdsAsync(IEnumerable<string> ids);
    public Task<bool> ExistsAsync(string id);
    public Task<bool> DeleteUserAsync(string userId);
}
