using PGAN.Poracle.Web.Core.Abstractions.Repositories;
using PGAN.Poracle.Web.Core.Abstractions.Services;
using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Core.Services;

public class HumanService : IHumanService
{
    private readonly IHumanRepository _repository;

    public HumanService(IHumanRepository repository)
    {
        _repository = repository;
    }

    public async Task<IEnumerable<Human>> GetAllAsync()
    {
        return await _repository.GetAllAsync();
    }

    public async Task<Human?> GetByIdAsync(string id)
    {
        return await _repository.GetByIdAsync(id);
    }

    public async Task<Human?> GetByIdAndProfileAsync(string id, int profileNo)
    {
        return await _repository.GetByIdAndProfileAsync(id, profileNo);
    }

    public async Task<Human> CreateAsync(Human human)
    {
        return await _repository.CreateAsync(human);
    }

    public async Task<Human> UpdateAsync(Human human)
    {
        return await _repository.UpdateAsync(human);
    }

    public async Task<bool> ExistsAsync(string id)
    {
        return await _repository.ExistsAsync(id);
    }

    public async Task<int> DeleteAllAlarmsByUserAsync(string userId)
    {
        return await _repository.DeleteAllAlarmsByUserAsync(userId);
    }
}
