using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

public interface IInvasionService
{
    public Task<IEnumerable<Invasion>> GetByUserAsync(string userId, int profileNo);
    public Task<Invasion?> GetByUidAsync(string userId, int uid);

    /// <summary>The rule with this uid of the stated kind ("event" or "invasion"); another kind matches any.</summary>
    public Task<Invasion?> GetByUidAsync(string userId, int uid, string? kind);
    public Task<Invasion> CreateAsync(string userId, Invasion model);
    public Task<Invasion> UpdateAsync(string userId, Invasion model);

    /// <summary>
    /// <paramref name="storedKind"/> is the kind ("event" or "invasion") of the rule the caller opened. When the
    /// model's grunt is of the other kind, the rule is created as the new kind and the old one removed, instead of
    /// a write addressed by the old uid. Null keeps <see cref="UpdateAsync(string, Invasion)"/>.
    /// </summary>
    public Task<Invasion> UpdateAsync(string userId, Invasion model, string? storedKind);
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
