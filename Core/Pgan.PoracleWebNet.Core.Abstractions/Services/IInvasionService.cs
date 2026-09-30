using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

public interface IInvasionService
{
    public Task<IEnumerable<Invasion>> GetByUserAsync(string userId, int profileNo);
    public Task<Invasion?> GetByUidAsync(string userId, int uid);
    public Task<Invasion> CreateAsync(string userId, Invasion model);
    public Task<Invasion> UpdateAsync(string userId, Invasion model);
    public Task<bool> DeleteAsync(string userId, int uid);
    public Task<int> DeleteAllByUserAsync(string userId, int profileNo);
    public Task<DistanceUpdateResult> UpdateDistanceByUserAsync(string userId, int profileNo, int distance);
    public Task<DistanceUpdateResult> UpdateDistanceByUidsAsync(List<int> uids, string userId, int distance);
    public Task<int> CountByUserAsync(string userId, int profileNo);
    public Task<IEnumerable<Invasion>> BulkCreateAsync(string userId, IEnumerable<Invasion> models);

    /// <summary>
    /// True when <paramref name="gruntType"/> names a Pokestop event (kecleon, gold-stop, showcase) and this
    /// server has a Pokestop Events page for it, so a rule written here would never show on the invasion list.
    /// </summary>
    public Task<bool> BelongsToPokestopEventsAsync(string? gruntType);
}
