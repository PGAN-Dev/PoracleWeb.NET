using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Abstractions.Repositories;

public interface IProfileRepository
{
    public Task<IEnumerable<Profile>> GetByUserAsync(string userId);
    public Task<Profile?> GetByUserAndProfileNoAsync(string userId, int profileNo);
    public Task<Profile> CreateAsync(Profile profile);
    public Task<Profile> UpdateAsync(Profile profile);
    /// <summary>
    /// Renames a profile, touching only <c>profiles.name</c>.
    /// </summary>
    /// <remarks>
    /// PoracleNG's profile update handler silently ignores the <c>name</c> key — it answers
    /// <c>{"status":"ok"}</c> and writes nothing, while honouring <c>active_hours</c> on the same request.
    /// So rename cannot go through the proxy. Verified that a direct write is served by PoracleNG's own
    /// read immediately afterwards, so nothing else needs invalidating. Deliberately narrower than
    /// <see cref="UpdateAsync"/>, which also rewrites area and coordinates. See #406.
    /// </remarks>
    /// <returns><c>false</c> if no such profile exists.</returns>
    public Task<bool> RenameAsync(string userId, int profileNo, string name);

    public Task<bool> DeleteAsync(string userId, int profileNo);
}
