using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Core.Abstractions.Services;

public interface IRaidService
{
    Task<IEnumerable<Raid>> GetByUserAsync(string userId, int profileNo);
    Task<Raid?> GetByUidAsync(int uid);
    Task<Raid> CreateAsync(string userId, Raid model);
    Task<Raid> UpdateAsync(Raid model);
    Task<bool> DeleteAsync(int uid);
    Task<int> DeleteAllByUserAsync(string userId, int profileNo);
    Task<int> UpdateDistanceByUserAsync(string userId, int profileNo, int distance);
    Task<int> CountByUserAsync(string userId, int profileNo);
}
