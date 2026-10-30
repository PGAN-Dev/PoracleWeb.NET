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

    /// <summary>
    /// Throws <see cref="AlarmValidationException"/> when <paramref name="areas"/>, as a profile's area list, would
    /// break PoracleNG's <c>[tracking] max_areas</c>. <paramref name="previous"/> is the list it replaces: one already
    /// over the limit may be kept or shrunk, never grown. Null means there is none (a new profile).
    /// </summary>
    public Task EnsureAreaListWithinLimitAsync(
        string humanId,
        IReadOnlyCollection<string> areas,
        Func<Task<IReadOnlyCollection<string>>>? previous);

    /// <summary>
    /// <see cref="EnsureAreaListWithinLimitAsync"/> for one alarm's override areas. <paramref name="storedOnRule"/>
    /// is the stored rule's list on an edit, and null on a create.
    /// </summary>
    public Task EnsureOverrideAreasWithinLimitAsync(
        string humanId,
        IReadOnlyCollection<string> areas,
        Func<Task<IReadOnlyCollection<string>>>? storedOnRule);
}
