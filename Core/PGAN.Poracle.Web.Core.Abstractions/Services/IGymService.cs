using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Core.Abstractions.Services;

public interface IGymService
{
    Task<IEnumerable<Gym>> GetByUserAsync(string userId, int profileNo);
    Task<Gym?> GetByUidAsync(int uid);
    Task<Gym> CreateAsync(string userId, Gym model);
    Task<Gym> UpdateAsync(Gym model);
    Task<bool> DeleteAsync(int uid);
    Task<int> DeleteAllByUserAsync(string userId, int profileNo);
    Task<int> UpdateDistanceByUserAsync(string userId, int profileNo, int distance);
    Task<int> CountByUserAsync(string userId, int profileNo);
}
