using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Core.Abstractions.Services;

public interface IGymService
{
    public Task<IEnumerable<Gym>> GetByUserAsync(string userId, int profileNo);
    public Task<Gym?> GetByUidAsync(int uid);
    public Task<Gym> CreateAsync(string userId, Gym model);
    public Task<Gym> UpdateAsync(Gym model);
    public Task<bool> DeleteAsync(int uid);
    public Task<int> DeleteAllByUserAsync(string userId, int profileNo);
    public Task<int> UpdateDistanceByUserAsync(string userId, int profileNo, int distance);
    public Task<int> CountByUserAsync(string userId, int profileNo);
    public Task<IEnumerable<Gym>> BulkCreateAsync(string userId, IEnumerable<Gym> models);
}
