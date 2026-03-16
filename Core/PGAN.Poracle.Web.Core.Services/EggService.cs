using PGAN.Poracle.Web.Core.Abstractions.Repositories;
using PGAN.Poracle.Web.Core.Abstractions.Services;
using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Core.Services;

public class EggService : IEggService
{
    private readonly IEggRepository _repository;

    public EggService(IEggRepository repository)
    {
        _repository = repository;
    }

    public async Task<IEnumerable<Egg>> GetByUserAsync(string userId, int profileNo)
    {
        return await _repository.GetByUserAsync(userId, profileNo);
    }

    public async Task<Egg?> GetByUidAsync(int uid)
    {
        return await _repository.GetByUidAsync(uid);
    }

    public async Task<Egg> CreateAsync(string userId, Egg model)
    {
        model.Id = userId;
        return await _repository.CreateAsync(model);
    }

    public async Task<Egg> UpdateAsync(Egg model)
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
