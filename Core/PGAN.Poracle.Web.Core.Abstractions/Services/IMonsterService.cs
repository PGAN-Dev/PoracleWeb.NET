using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Core.Abstractions.Services;

public interface IMonsterService
{
    public Task<IEnumerable<Monster>> GetByUserAsync(string userId, int profileNo);
    public Task<Monster?> GetByUidAsync(int uid);
    public Task<Monster> CreateAsync(string userId, Monster model);
    public Task<Monster> UpdateAsync(Monster model);
    public Task<bool> DeleteAsync(int uid);
    public Task<int> DeleteAllByUserAsync(string userId, int profileNo);
    public Task<int> UpdateDistanceByUserAsync(string userId, int profileNo, int distance);
    public Task<int> UpdateDistanceByUidsAsync(List<int> uids, string userId, int distance);
    public Task<int> CountByUserAsync(string userId, int profileNo);
    public Task<IEnumerable<Monster>> BulkCreateAsync(string userId, IEnumerable<Monster> models);
}
