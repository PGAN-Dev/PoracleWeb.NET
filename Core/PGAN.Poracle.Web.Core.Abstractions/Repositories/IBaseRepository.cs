namespace PGAN.Poracle.Web.Core.Abstractions.Repositories;

public interface IBaseRepository<TModel> where TModel : class
{
    public Task<IEnumerable<TModel>> GetByUserAsync(string userId, int profileNo);
    public Task<TModel?> GetByUidAsync(int uid);
    public Task<TModel> CreateAsync(TModel model);
    public Task<TModel> UpdateAsync(TModel model);
    public Task<bool> DeleteAsync(int uid);
    public Task<int> DeleteAllByUserAsync(string userId, int profileNo);
    public Task<int> UpdateDistanceByUserAsync(string userId, int profileNo, int distance);
    public Task<int> UpdateDistanceByUidsAsync(List<int> uids, string userId, int distance);
    public Task<int> CountByUserAsync(string userId, int profileNo);
    public Task<int> BulkUpdateCleanAsync(string userId, int profileNo, int clean);
    public Task<IEnumerable<TModel>> BulkCreateAsync(IEnumerable<TModel> models);
}
