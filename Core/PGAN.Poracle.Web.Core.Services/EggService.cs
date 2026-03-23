using PGAN.Poracle.Web.Core.Abstractions.Repositories;
using PGAN.Poracle.Web.Core.Abstractions.Services;
using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Core.Services;

public class EggService(IEggRepository repository) : IEggService
{
    private readonly IEggRepository _repository = repository;

    public async Task<IEnumerable<Egg>> GetByUserAsync(string userId, int profileNo) => await this._repository.GetByUserAsync(userId, profileNo);

    public async Task<Egg?> GetByUidAsync(int uid) => await this._repository.GetByUidAsync(uid);

    public async Task<Egg> CreateAsync(string userId, Egg model)
    {
        model.Id = userId;
        return await this._repository.CreateAsync(model);
    }

    public async Task<Egg> UpdateAsync(Egg model) => await this._repository.UpdateAsync(model);

    public async Task<bool> DeleteAsync(int uid) => await this._repository.DeleteAsync(uid);

    public async Task<int> DeleteAllByUserAsync(string userId, int profileNo) => await this._repository.DeleteAllByUserAsync(userId, profileNo);

    public async Task<int> UpdateDistanceByUserAsync(string userId, int profileNo, int distance) => await this._repository.UpdateDistanceByUserAsync(userId, profileNo, distance);

    public async Task<int> UpdateDistanceByUidsAsync(List<int> uids, string userId, int distance) => await this._repository.UpdateDistanceByUidsAsync(uids, userId, distance);

    public async Task<int> CountByUserAsync(string userId, int profileNo) => await this._repository.CountByUserAsync(userId, profileNo);

    public async Task<IEnumerable<Egg>> BulkCreateAsync(string userId, IEnumerable<Egg> models)
    {
        foreach (var model in models)
        {
            model.Id = userId;
        }

        return await this._repository.BulkCreateAsync(models);
    }
}
