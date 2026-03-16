using PGAN.Poracle.Web.Core.Abstractions.Repositories;
using PGAN.Poracle.Web.Core.Abstractions.Services;
using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Core.Services;

public class PwebSettingService : IPwebSettingService
{
    private readonly IPwebSettingRepository _repository;

    public PwebSettingService(IPwebSettingRepository repository)
    {
        _repository = repository;
    }

    public async Task<IEnumerable<PwebSetting>> GetAllAsync()
    {
        return await _repository.GetAllAsync();
    }

    public async Task<PwebSetting?> GetByKeyAsync(string key)
    {
        return await _repository.GetByKeyAsync(key);
    }

    public async Task<PwebSetting> CreateOrUpdateAsync(PwebSetting setting)
    {
        return await _repository.CreateOrUpdateAsync(setting);
    }

    public async Task<bool> DeleteAsync(string key)
    {
        return await _repository.DeleteAsync(key);
    }
}
