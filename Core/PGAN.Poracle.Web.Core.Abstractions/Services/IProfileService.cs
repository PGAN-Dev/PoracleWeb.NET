using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Core.Abstractions.Services;

public interface IProfileService
{
    public Task<IEnumerable<Profile>> GetByUserAsync(string userId);
    public Task<Profile?> GetByUserAndProfileNoAsync(string userId, int profileNo);
    public Task<Profile> CreateAsync(Profile profile);
    public Task<Profile> UpdateAsync(Profile profile);
    public Task<bool> DeleteAsync(string userId, int profileNo);
}
