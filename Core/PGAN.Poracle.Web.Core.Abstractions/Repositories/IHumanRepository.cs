using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Core.Abstractions.Repositories;

public interface IHumanRepository
{
    public Task<IEnumerable<Human>> GetAllAsync();
    public Task<Human?> GetByIdAsync(string id);
    public Task<Human?> GetByIdAndProfileAsync(string id, int profileNo);
    public Task<Human> CreateAsync(Human human);
    public Task<Human> UpdateAsync(Human human);
    public Task<bool> ExistsAsync(string id);
    public Task<int> DeleteAllAlarmsByUserAsync(string userId);
    public Task<bool> DeleteUserAsync(string userId);
}
