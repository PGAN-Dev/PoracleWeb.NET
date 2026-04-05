using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

public interface IMaxBattleService
{
    public Task<IEnumerable<MaxBattle>> GetByUserAsync(string userId, int profileNo);
    public Task<MaxBattle?> GetByUidAsync(string userId, int uid);
    public Task<MaxBattle> CreateAsync(string userId, MaxBattle model);
    public Task<MaxBattle> UpdateAsync(string userId, MaxBattle model);
    public Task<bool> DeleteAsync(string userId, int uid);
    public Task<int> DeleteAllByUserAsync(string userId, int profileNo);
    public Task<int> UpdateDistanceByUserAsync(string userId, int profileNo, int distance);
    public Task<int> UpdateDistanceByUidsAsync(List<int> uids, string userId, int distance);
    public Task<int> CountByUserAsync(string userId, int profileNo);
    public Task<IEnumerable<MaxBattle>> BulkCreateAsync(string userId, IEnumerable<MaxBattle> models);
}
