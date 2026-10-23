using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

public interface IInvasionService : IAlarmService<Invasion>
{
    /// <summary>
    /// True when <paramref name="gruntType"/> names a Pokestop event (kecleon, gold-stop, showcase) and this
    /// server has a Pokestop Events page for it, so a rule written here would never show on the invasion list.
    /// </summary>
    public Task<bool> BelongsToPokestopEventsAsync(string? gruntType);
}
