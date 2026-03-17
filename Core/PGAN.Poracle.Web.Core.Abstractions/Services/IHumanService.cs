using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Core.Abstractions.Services;

public interface IHumanService
{
    Task<IEnumerable<Human>> GetAllAsync();
    Task<Human?> GetByIdAsync(string id);
    Task<Human?> GetByIdAndProfileAsync(string id, int profileNo);
    Task<Human> CreateAsync(Human human);
    Task<Human> UpdateAsync(Human human);
    Task<bool> ExistsAsync(string id);
    Task<int> DeleteAllAlarmsByUserAsync(string userId);
    Task<bool> DeleteUserAsync(string userId);
}
