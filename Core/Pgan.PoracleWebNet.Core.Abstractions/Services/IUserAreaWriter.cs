using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

/// <summary>
/// Writes a user's area subscriptions for the active profile through PoracleNG's trusted v2 route, after
/// PoracleWeb.NET's own authorisation (a trusted write skips PoracleNG's).
/// </summary>
public interface IUserAreaWriter
{
    /// <summary>
    /// Replaces the active profile's list with <paramref name="requested"/> (lowercased, trimmed).
    /// A name is allowed when PoracleNG lists it <c>userSelectable</c> for the human, it is one of the
    /// human's own drawn fences, or the profile already holds it. Throws
    /// <see cref="AreaNotPermittedException"/> naming the rest, before anything is posted.
    /// </summary>
    public Task<AreaSaveResult> SaveSelectionAsync(string humanId, IReadOnlyCollection<string> requested);

    /// <summary>
    /// The names in <paramref name="requested"/> the human may subscribe to, by the same rule as
    /// <see cref="SaveSelectionAsync"/> (lowercased, trimmed, distinct, request order). The rest are dropped
    /// with a warning rather than refused; nothing is written.
    /// </summary>
    public Task<IReadOnlyList<string>> FilterAllowedAsync(string humanId, IReadOnlyCollection<string> requested);

    /// <summary>
    /// Adds one name to the active profile. False when already present or PoracleNG rejected it.
    /// Does not authorise the name: the caller must have (for a user fence, that the user owns it).
    /// </summary>
    public Task<bool> AddToActiveProfileAsync(string humanId, string name);

    /// <summary>
    /// Removes one name from the active profile. False when it was not there.
    /// Does not authorise the name: the caller must have (for a user fence, that the user owns it).
    /// </summary>
    public Task<bool> RemoveFromActiveProfileAsync(string humanId, string name);

    /// <summary>
    /// Asks PoracleNG, without writing anything, which of the human's rules would widen if
    /// <paramref name="name"/> were removed everywhere: the rules whose only override area it is.
    /// </summary>
    public Task<AreaDeleteImpact> PreviewRemovalAsync(string humanId, string name);

    /// <summary>
    /// Removes <paramref name="name"/> from every profile's area list and from every rule's override areas.
    /// With <paramref name="deleteWidenedAlarms"/>, the rules that would widen are deleted first, on every
    /// profile, so none of them is left matching across the profile's areas. Throws when PoracleNG refuses
    /// or cannot be reached; the caller must not treat the name as gone in that case.
    /// </summary>
    public Task RemoveEverywhereAsync(string humanId, string name, bool deleteWidenedAlarms);

    /// <summary>
    /// Renames <paramref name="from"/> to <paramref name="to"/> on every profile and in every rule's
    /// override areas, in one PoracleNG call. False when PoracleNG refused it (<paramref name="to"/> is not
    /// a fence it has loaded: the caller must reload its geofences first); logged, never thrown for that.
    /// Other failures propagate.
    /// </summary>
    public Task<bool> RenameEverywhereAsync(string humanId, string from, string to);
}
