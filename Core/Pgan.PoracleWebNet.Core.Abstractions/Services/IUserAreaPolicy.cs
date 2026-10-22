using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

/// <summary>
/// The one rule for which areas a human may name: on an area subscription and on an alarm's override areas.
/// </summary>
public interface IUserAreaPolicy
{
    /// <summary>
    /// The names a human may use: PoracleNG's <c>userSelectable</c> areas for them, their own drawn fences,
    /// and what the active profile already holds. Keyed as PoracleNG keys them (lowercase, <c>_</c> = space).
    /// </summary>
    public Task<IReadOnlySet<string>> AllowedKeysAsync(string humanId);

    /// <summary>
    /// Throws <see cref="AlarmValidationException"/> naming each of <paramref name="requested"/> that is
    /// outside <see cref="AllowedKeysAsync"/>. Names <paramref name="storedOnRule"/> returns are allowed too
    /// (a rule being edited keeps what it already holds); it is called only when a name would otherwise be refused.
    /// </summary>
    public Task EnsureOverrideAreasAllowedAsync(
        string humanId,
        IReadOnlyCollection<string> requested,
        Func<Task<IReadOnlyCollection<string>>>? storedOnRule);
}
