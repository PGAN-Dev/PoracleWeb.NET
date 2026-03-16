using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Core.Abstractions.Services;

public interface IInvasionService
{
    Task<IEnumerable<Invasion>> GetByUserAsync(string userId, int profileNo);
    Task<Invasion?> GetByUidAsync(int uid);
    Task<Invasion> CreateAsync(string userId, Invasion model);
    Task<Invasion> UpdateAsync(Invasion model);
    Task<bool> DeleteAsync(int uid);
    Task<int> DeleteAllByUserAsync(string userId, int profileNo);
    Task<int> UpdateDistanceByUserAsync(string userId, int profileNo, int distance);
    Task<int> CountByUserAsync(string userId, int profileNo);
}
