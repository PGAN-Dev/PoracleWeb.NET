using PGAN.Poracle.Web.Core.Abstractions.Repositories;
using PGAN.Poracle.Web.Core.Abstractions.Services;
using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Core.Services;

public class GymService(IGymRepository repository) : IGymService
{
    private readonly IGymRepository _repository = repository;

    public async Task<IEnumerable<Gym>> GetByUserAsync(string userId, int profileNo) => await this._repository.GetByUserAsync(userId, profileNo);

    public async Task<Gym?> GetByUidAsync(int uid) => await this._repository.GetByUidAsync(uid);

    public async Task<Gym> CreateAsync(string userId, Gym model)
    {
        model.Id = userId;
        return await this._repository.CreateAsync(model);
    }

    public async Task<Gym> UpdateAsync(Gym model) => await this._repository.UpdateAsync(model);

    public async Task<bool> DeleteAsync(int uid) => await this._repository.DeleteAsync(uid);

    public async Task<int> DeleteAllByUserAsync(string userId, int profileNo) => await this._repository.DeleteAllByUserAsync(userId, profileNo);

    public async Task<int> UpdateDistanceByUserAsync(string userId, int profileNo, int distance) => await this._repository.UpdateDistanceByUserAsync(userId, profileNo, distance);

    public async Task<int> UpdateDistanceByUidsAsync(List<int> uids, string userId, int distance) => await this._repository.UpdateDistanceByUidsAsync(uids, userId, distance);

    public async Task<int> CountByUserAsync(string userId, int profileNo) => await this._repository.CountByUserAsync(userId, profileNo);

    public async Task<IEnumerable<Gym>> BulkCreateAsync(string userId, IEnumerable<Gym> models)
    {
        foreach (var model in models)
        {
            model.Id = userId;
        }

        return await this._repository.BulkCreateAsync(models);
    }
}
