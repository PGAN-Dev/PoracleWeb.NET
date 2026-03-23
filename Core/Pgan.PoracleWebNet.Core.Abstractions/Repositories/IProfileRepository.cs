using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Abstractions.Repositories;

public interface IProfileRepository
{
    public Task<IEnumerable<Profile>> GetByUserAsync(string userId);
    public Task<Profile?> GetByUserAndProfileNoAsync(string userId, int profileNo);
    public Task<Profile> CreateAsync(Profile profile);
    public Task<Profile> UpdateAsync(Profile profile);
    public Task<bool> DeleteAsync(string userId, int profileNo);
}
