using PGAN.Poracle.Web.Core.Abstractions.Repositories;
using PGAN.Poracle.Web.Core.Abstractions.Services;
using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Core.Services;

public class GymService : IGymService
{
    private readonly IGymRepository _repository;

    public GymService(IGymRepository repository)
    {
        _repository = repository;
    }

    public async Task<IEnumerable<Gym>> GetByUserAsync(string userId, int profileNo)
    {
        return await _repository.GetByUserAsync(userId, profileNo);
    }

    public async Task<Gym?> GetByUidAsync(int uid)
    {
        return await _repository.GetByUidAsync(uid);
    }

    public async Task<Gym> CreateAsync(string userId, Gym model)
    {
        model.Id = userId;
        return await _repository.CreateAsync(model);
    }

    public async Task<Gym> UpdateAsync(Gym model)
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
