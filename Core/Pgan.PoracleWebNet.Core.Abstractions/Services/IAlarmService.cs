using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

/// <summary>The calls every alarm type's service answers, so one router can serve all eleven.</summary>
public interface IAlarmService<TModel>
{
    public Task<IEnumerable<TModel>> GetByUserAsync(string userId, int profileNo);
    public Task<TModel?> GetByUidAsync(string userId, int uid);
    public Task<TModel> CreateAsync(string userId, TModel model);
    public Task<TModel> UpdateAsync(string userId, TModel model);
    public Task<bool> DeleteAsync(string userId, int uid);
    public Task<int> DeleteAllByUserAsync(string userId, int profileNo);
    public Task<DistanceUpdateResult> UpdateDistanceByUserAsync(string userId, int profileNo, int distance);
    public Task<DistanceUpdateResult> UpdateDistanceByUidsAsync(List<int> uids, string userId, int distance);
    public Task<int> CountByUserAsync(string userId, int profileNo);
    public Task<IEnumerable<TModel>> BulkCreateAsync(string userId, IEnumerable<TModel> models);
}
