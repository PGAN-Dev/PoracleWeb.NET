using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

public interface INestService
{
    public Task<IEnumerable<Nest>> GetByUserAsync(string userId, int profileNo);
    public Task<Nest?> GetByUidAsync(string userId, int uid);
    public Task<Nest> CreateAsync(string userId, Nest model);
    public Task<Nest> UpdateAsync(string userId, Nest model);
    public Task<bool> DeleteAsync(string userId, int uid);
    public Task<int> DeleteAllByUserAsync(string userId, int profileNo);
    public Task<int> UpdateDistanceByUserAsync(string userId, int profileNo, int distance);
    public Task<int> UpdateDistanceByUidsAsync(List<int> uids, string userId, int distance);
    public Task<int> CountByUserAsync(string userId, int profileNo);
    public Task<IEnumerable<Nest>> BulkCreateAsync(string userId, IEnumerable<Nest> models);
}
