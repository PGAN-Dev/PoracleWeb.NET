using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Core.Abstractions.Repositories;

public interface IProfileRepository
{
    Task<IEnumerable<Profile>> GetByUserAsync(string userId);
    Task<Profile?> GetByUserAndProfileNoAsync(string userId, int profileNo);
    Task<Profile> CreateAsync(Profile profile);
    Task<Profile> UpdateAsync(Profile profile);
    Task<bool> DeleteAsync(string userId, int profileNo);
}
