using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Core.Abstractions.Services;

public interface IPwebSettingService
{
    public Task<IEnumerable<PwebSetting>> GetAllAsync();
    public Task<PwebSetting?> GetByKeyAsync(string key);
    public Task<PwebSetting> CreateOrUpdateAsync(PwebSetting setting);
    public Task<bool> DeleteAsync(string key);
}
