using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Core.Abstractions.Services;

public interface IPwebSettingService
{
    Task<IEnumerable<PwebSetting>> GetAllAsync();
    Task<PwebSetting?> GetByKeyAsync(string key);
    Task<PwebSetting> CreateOrUpdateAsync(PwebSetting setting);
    Task<bool> DeleteAsync(string key);
}
