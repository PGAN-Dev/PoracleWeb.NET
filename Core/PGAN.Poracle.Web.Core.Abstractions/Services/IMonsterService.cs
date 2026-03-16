using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Core.Abstractions.Services;

public interface IMonsterService
{
    Task<IEnumerable<Monster>> GetByUserAsync(string userId, int profileNo);
    Task<Monster?> GetByUidAsync(int uid);
    Task<Monster> CreateAsync(string userId, Monster model);
    Task<Monster> UpdateAsync(Monster model);
    Task<bool> DeleteAsync(int uid);
    Task<int> DeleteAllByUserAsync(string userId, int profileNo);
    Task<int> UpdateDistanceByUserAsync(string userId, int profileNo, int distance);
    Task<int> CountByUserAsync(string userId, int profileNo);
}
