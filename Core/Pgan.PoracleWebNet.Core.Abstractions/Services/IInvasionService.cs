using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

public interface IInvasionService : IAlarmService<Invasion>
{
    /// <summary>The rule with this uid of the stated kind ("event" or "invasion"); another kind matches any.</summary>
    public Task<Invasion?> GetByUidAsync(string userId, int uid, string? kind);

    /// <summary>
    /// <paramref name="storedKind"/> is the kind ("event" or "invasion") of the rule the caller opened. When the
    /// model's grunt is of the other kind, the rule is created as the new kind and the old one removed, instead of
    /// a write addressed by the old uid. Null keeps <see cref="IAlarmService{TModel}.UpdateAsync(string, TModel)"/>.
    /// </summary>
    public Task<Invasion> UpdateAsync(string userId, Invasion model, string? storedKind);

    /// <summary>
    /// Deletes the rule with this uid of the stated kind ("event" or "invasion"). An event goes straight to the
    /// incident route, so an invasion sharing the number is never touched; any other kind deletes as
    /// <see cref="IAlarmService{TModel}.DeleteAsync(string, int)"/> does.
    /// </summary>
    public Task<bool> DeleteAsync(string userId, int uid, string? kind);

    /// <summary>
    /// True when <paramref name="gruntType"/> names a Pokestop event (kecleon, gold-stop, showcase) and this
    /// server has a Pokestop Events page for it, so a rule written here would never show on the invasion list.
    /// </summary>
    public Task<bool> BelongsToPokestopEventsAsync(string? gruntType);
}
