using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

public interface IQuestService
{
    public Task<IEnumerable<Quest>> GetByUserAsync(string userId, int profileNo);
    public Task<Quest?> GetByUidAsync(string userId, int uid);
    public Task<Quest> CreateAsync(string userId, Quest model);
    public Task<Quest> UpdateAsync(string userId, Quest model);
    public Task<bool> DeleteAsync(string userId, int uid);
    public Task<int> DeleteAllByUserAsync(string userId, int profileNo);
    public Task<int> UpdateDistanceByUserAsync(string userId, int profileNo, int distance);
    public Task<int> UpdateDistanceByUidsAsync(List<int> uids, string userId, int distance);
    public Task<int> CountByUserAsync(string userId, int profileNo);
    public Task<IEnumerable<Quest>> BulkCreateAsync(string userId, IEnumerable<Quest> models);
}
