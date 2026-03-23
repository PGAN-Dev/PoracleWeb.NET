using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

public interface IPwebSettingService
{
    public Task<IEnumerable<PwebSetting>> GetAllAsync();
    public Task<PwebSetting?> GetByKeyAsync(string key);
    public Task<PwebSetting> CreateOrUpdateAsync(PwebSetting setting);
    public Task<bool> DeleteAsync(string key);
}
