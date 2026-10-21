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

    /// <summary>Adds one name to the active profile. False when already present or PoracleNG rejected it.</summary>
    public Task<bool> AddToActiveProfileAsync(string humanId, string name);

    /// <summary>Removes one name from the active profile. False when it was not there.</summary>
    public Task<bool> RemoveFromActiveProfileAsync(string humanId, string name);
}
