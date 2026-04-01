using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

public interface IGymService
{
    public Task<IEnumerable<Gym>> GetByUserAsync(string userId, int profileNo);
    public Task<Gym?> GetByUidAsync(string userId, int uid);
    public Task<Gym> CreateAsync(string userId, Gym model);
    public Task<Gym> UpdateAsync(string userId, Gym model);
    public Task<bool> DeleteAsync(string userId, int uid);
    public Task<int> DeleteAllByUserAsync(string userId, int profileNo);
    public Task<int> UpdateDistanceByUserAsync(string userId, int profileNo, int distance);
    public Task<int> UpdateDistanceByUidsAsync(List<int> uids, string userId, int distance);
    public Task<int> CountByUserAsync(string userId, int profileNo);
    public Task<IEnumerable<Gym>> BulkCreateAsync(string userId, IEnumerable<Gym> models);
}
