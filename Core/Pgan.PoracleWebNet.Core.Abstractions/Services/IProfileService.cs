using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

public interface IProfileService
{
    public Task<IEnumerable<Profile>> GetByUserAsync(string userId);
    public Task<Profile?> GetByUserAndProfileNoAsync(string userId, int profileNo);
}
