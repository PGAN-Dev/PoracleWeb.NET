using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

public interface IFortChangeService
{
    public Task<IEnumerable<FortChange>> GetByUserAsync(string userId, int profileNo);
    public Task<FortChange?> GetByUidAsync(string userId, int uid);
    public Task<FortChange> CreateAsync(string userId, FortChange model);
    public Task<FortChange> UpdateAsync(string userId, FortChange model);
    public Task<bool> DeleteAsync(string userId, int uid);
    public Task<int> DeleteAllByUserAsync(string userId, int profileNo);
    public Task<int> UpdateDistanceByUserAsync(string userId, int profileNo, int distance);
    public Task<int> UpdateDistanceByUidsAsync(List<int> uids, string userId, int distance);
    public Task<int> CountByUserAsync(string userId, int profileNo);
    public Task<IEnumerable<FortChange>> BulkCreateAsync(string userId, IEnumerable<FortChange> models);
}
