using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Core.Abstractions.Services;

public interface IEggService
{
    Task<IEnumerable<Egg>> GetByUserAsync(string userId, int profileNo);
    Task<Egg?> GetByUidAsync(int uid);
    Task<Egg> CreateAsync(string userId, Egg model);
    Task<Egg> UpdateAsync(Egg model);
    Task<bool> DeleteAsync(int uid);
    Task<int> DeleteAllByUserAsync(string userId, int profileNo);
    Task<int> UpdateDistanceByUserAsync(string userId, int profileNo, int distance);
    Task<int> CountByUserAsync(string userId, int profileNo);
}
