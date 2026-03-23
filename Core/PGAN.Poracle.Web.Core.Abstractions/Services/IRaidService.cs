using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Core.Abstractions.Services;

public interface IRaidService
{
    public Task<IEnumerable<Raid>> GetByUserAsync(string userId, int profileNo);
    public Task<Raid?> GetByUidAsync(int uid);
    public Task<Raid> CreateAsync(string userId, Raid model);
    public Task<Raid> UpdateAsync(Raid model);
    public Task<bool> DeleteAsync(int uid);
    public Task<int> DeleteAllByUserAsync(string userId, int profileNo);
    public Task<int> UpdateDistanceByUserAsync(string userId, int profileNo, int distance);
    public Task<int> UpdateDistanceByUidsAsync(List<int> uids, string userId, int distance);
    public Task<int> CountByUserAsync(string userId, int profileNo);
    public Task<IEnumerable<Raid>> BulkCreateAsync(string userId, IEnumerable<Raid> models);
}
