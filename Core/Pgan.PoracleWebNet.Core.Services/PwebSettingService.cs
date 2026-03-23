using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

public class PwebSettingService(IPwebSettingRepository repository) : IPwebSettingService
{
    private readonly IPwebSettingRepository _repository = repository;

    public async Task<IEnumerable<PwebSetting>> GetAllAsync() => await this._repository.GetAllAsync();

    public async Task<PwebSetting?> GetByKeyAsync(string key) => await this._repository.GetByKeyAsync(key);

    public async Task<PwebSetting> CreateOrUpdateAsync(PwebSetting setting) => await this._repository.CreateOrUpdateAsync(setting);

    public async Task<bool> DeleteAsync(string key) => await this._repository.DeleteAsync(key);
}
