using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

/// <summary>Pokestop-event alarms: Showcase, Kecleon and Gold Stop.</summary>
public interface IPokestopEventService
{
    public Task<IEnumerable<PokestopEvent>> GetByUserAsync(string userId, int profileNo);
    public Task<PokestopEvent?> GetByUidAsync(string userId, int uid);
    public Task<PokestopEvent> CreateAsync(string userId, PokestopEvent model);
    public Task<PokestopEvent> UpdateAsync(string userId, PokestopEvent model);
    public Task<bool> DeleteAsync(string userId, int uid);
    public Task<int> DeleteAllByUserAsync(string userId, int profileNo);
    public Task<int> UpdateDistanceByUserAsync(string userId, int profileNo, int distance);
    public Task<int> UpdateDistanceByUidsAsync(List<int> uids, string userId, int distance);
    public Task<int> CountByUserAsync(string userId, int profileNo);
    public Task<IEnumerable<PokestopEvent>> BulkCreateAsync(string userId, IEnumerable<PokestopEvent> models);
}
