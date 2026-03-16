using PGAN.Poracle.Web.Core.Abstractions.Repositories;
using PGAN.Poracle.Web.Core.Abstractions.Services;
using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Core.Services;

public class LureService : ILureService
{
    private readonly ILureRepository _repository;

    public LureService(ILureRepository repository)
    {
        _repository = repository;
    }

    public async Task<IEnumerable<Lure>> GetByUserAsync(string userId, int profileNo)
    {
        return await _repository.GetByUserAsync(userId, profileNo);
    }

    public async Task<Lure?> GetByUidAsync(int uid)
    {
        return await _repository.GetByUidAsync(uid);
    }

    public async Task<Lure> CreateAsync(string userId, Lure model)
    {
        model.Id = userId;
        return await _repository.CreateAsync(model);
    }

    public async Task<Lure> UpdateAsync(Lure model)
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
