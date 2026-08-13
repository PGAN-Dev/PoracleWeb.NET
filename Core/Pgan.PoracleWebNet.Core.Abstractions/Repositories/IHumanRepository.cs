using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Abstractions.Repositories;

public interface IHumanRepository
{
    public Task<IEnumerable<Human>> GetAllAsync();
    public Task<Human?> GetByIdAsync(string id);
    public Task<Human> CreateAsync(Human human);
    public Task<Human> UpdateAsync(Human human);
    public Task<IEnumerable<Human>> GetByIdsAsync(IEnumerable<string> ids);
    public Task<bool> ExistsAsync(string id);
    public Task<bool> DeleteUserAsync(string userId);
}
