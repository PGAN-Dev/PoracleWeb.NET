using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Core.Abstractions.Services;

public interface ILureService
{
    Task<IEnumerable<Lure>> GetByUserAsync(string userId, int profileNo);
    Task<Lure?> GetByUidAsync(int uid);
    Task<Lure> CreateAsync(string userId, Lure model);
    Task<Lure> UpdateAsync(Lure model);
    Task<bool> DeleteAsync(int uid);
    Task<int> DeleteAllByUserAsync(string userId, int profileNo);
    Task<int> UpdateDistanceByUserAsync(string userId, int profileNo, int distance);
    Task<int> CountByUserAsync(string userId, int profileNo);
}
