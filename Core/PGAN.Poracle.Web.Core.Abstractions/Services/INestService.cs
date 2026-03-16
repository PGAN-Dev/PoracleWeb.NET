using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Core.Abstractions.Services;

public interface INestService
{
    Task<IEnumerable<Nest>> GetByUserAsync(string userId, int profileNo);
    Task<Nest?> GetByUidAsync(int uid);
    Task<Nest> CreateAsync(string userId, Nest model);
    Task<Nest> UpdateAsync(Nest model);
    Task<bool> DeleteAsync(int uid);
    Task<int> DeleteAllByUserAsync(string userId, int profileNo);
    Task<int> UpdateDistanceByUserAsync(string userId, int profileNo, int distance);
    Task<int> CountByUserAsync(string userId, int profileNo);
}
