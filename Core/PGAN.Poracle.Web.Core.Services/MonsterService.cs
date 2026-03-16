using PGAN.Poracle.Web.Core.Abstractions.Repositories;
using PGAN.Poracle.Web.Core.Abstractions.Services;
using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Core.Services;

public class MonsterService : IMonsterService
{
    private readonly IMonsterRepository _repository;

    public MonsterService(IMonsterRepository repository)
    {
        _repository = repository;
    }

    public async Task<IEnumerable<Monster>> GetByUserAsync(string userId, int profileNo)
    {
        return await _repository.GetByUserAsync(userId, profileNo);
    }

    public async Task<Monster?> GetByUidAsync(int uid)
    {
        return await _repository.GetByUidAsync(uid);
    }

    public async Task<Monster> CreateAsync(string userId, Monster model)
    {
        model.Id = userId;
        model.Ping ??= string.Empty;
        model.Template ??= string.Empty;
        return await _repository.CreateAsync(model);
    }

    public async Task<Monster> UpdateAsync(Monster model)
    {
        return await _repository.UpdateAsync(model);
    }

    public async Task<bool> DeleteAsync(int uid)
    {
        return await _repository.DeleteAsync(uid);
    }

    public async Task<int> DeleteAllByUserAsync(string userId, int profileNo)
    {
        return await _repository.DeleteAllByUserAsync(userId, profileNo);
    }

    public async Task<int> UpdateDistanceByUserAsync(string userId, int profileNo, int distance)
    {
        return await _repository.UpdateDistanceByUserAsync(userId, profileNo, distance);
    }

    public async Task<int> CountByUserAsync(string userId, int profileNo)
    {
        return await _repository.CountByUserAsync(userId, profileNo);
    }
}
