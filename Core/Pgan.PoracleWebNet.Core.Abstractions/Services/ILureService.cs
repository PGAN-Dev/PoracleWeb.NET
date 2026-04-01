using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

public interface ILureService
{
    public Task<IEnumerable<Lure>> GetByUserAsync(string userId, int profileNo);
    public Task<Lure?> GetByUidAsync(string userId, int uid);
    public Task<Lure> CreateAsync(string userId, Lure model);
    public Task<Lure> UpdateAsync(string userId, Lure model);
    public Task<bool> DeleteAsync(string userId, int uid);
    public Task<int> DeleteAllByUserAsync(string userId, int profileNo);
    public Task<int> UpdateDistanceByUserAsync(string userId, int profileNo, int distance);
    public Task<int> UpdateDistanceByUidsAsync(List<int> uids, string userId, int distance);
    public Task<int> CountByUserAsync(string userId, int profileNo);
    public Task<IEnumerable<Lure>> BulkCreateAsync(string userId, IEnumerable<Lure> models);
}
