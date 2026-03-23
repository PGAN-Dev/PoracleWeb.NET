using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

public class MonsterService(IMonsterRepository repository) : IMonsterService
{
    private readonly IMonsterRepository _repository = repository;

    public async Task<IEnumerable<Monster>> GetByUserAsync(string userId, int profileNo) => await this._repository.GetByUserAsync(userId, profileNo);

    public async Task<Monster?> GetByUidAsync(int uid) => await this._repository.GetByUidAsync(uid);

    public async Task<Monster> CreateAsync(string userId, Monster model)
    {
        model.Id = userId;
        model.Ping ??= string.Empty;
        model.Template ??= string.Empty;
        return await this._repository.CreateAsync(model);
    }

    public async Task<Monster> UpdateAsync(Monster model) => await this._repository.UpdateAsync(model);

    public async Task<bool> DeleteAsync(int uid) => await this._repository.DeleteAsync(uid);

    public async Task<int> DeleteAllByUserAsync(string userId, int profileNo) => await this._repository.DeleteAllByUserAsync(userId, profileNo);

    public async Task<int> UpdateDistanceByUserAsync(string userId, int profileNo, int distance) => await this._repository.UpdateDistanceByUserAsync(userId, profileNo, distance);

    public async Task<int> UpdateDistanceByUidsAsync(List<int> uids, string userId, int distance) => await this._repository.UpdateDistanceByUidsAsync(uids, userId, distance);

    public async Task<int> CountByUserAsync(string userId, int profileNo) => await this._repository.CountByUserAsync(userId, profileNo);

    public async Task<IEnumerable<Monster>> BulkCreateAsync(string userId, IEnumerable<Monster> models)
    {
        foreach (var model in models)
        {
            model.Id = userId;
            model.Ping ??= string.Empty;
            model.Template ??= string.Empty;
        }

        return await this._repository.BulkCreateAsync(models);
    }
}
