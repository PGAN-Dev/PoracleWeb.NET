namespace PGAN.Poracle.Web.Core.Abstractions.Repositories;

public interface IBaseRepository<TModel> where TModel : class
{
    Task<IEnumerable<TModel>> GetByUserAsync(string userId, int profileNo);
    Task<TModel?> GetByUidAsync(int uid);
    Task<TModel> CreateAsync(TModel model);
    Task<TModel> UpdateAsync(TModel model);
    Task<bool> DeleteAsync(int uid);
    Task<int> DeleteAllByUserAsync(string userId, int profileNo);
    Task<int> UpdateDistanceByUserAsync(string userId, int profileNo, int distance);
    Task<int> CountByUserAsync(string userId, int profileNo);
    Task<int> BulkUpdateCleanAsync(string userId, int profileNo, int clean);
}
