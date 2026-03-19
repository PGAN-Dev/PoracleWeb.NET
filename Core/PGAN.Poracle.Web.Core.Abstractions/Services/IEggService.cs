using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Core.Abstractions.Services;

public interface IEggService
{
    public Task<IEnumerable<Egg>> GetByUserAsync(string userId, int profileNo);
    public Task<Egg?> GetByUidAsync(int uid);
    public Task<Egg> CreateAsync(string userId, Egg model);
    public Task<Egg> UpdateAsync(Egg model);
    public Task<bool> DeleteAsync(int uid);
    public Task<int> DeleteAllByUserAsync(string userId, int profileNo);
    public Task<int> UpdateDistanceByUserAsync(string userId, int profileNo, int distance);
    public Task<int> CountByUserAsync(string userId, int profileNo);
    public Task<IEnumerable<Egg>> BulkCreateAsync(string userId, IEnumerable<Egg> models);
}
