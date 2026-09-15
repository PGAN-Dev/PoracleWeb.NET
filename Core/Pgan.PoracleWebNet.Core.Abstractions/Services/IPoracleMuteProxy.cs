using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

/// <summary>
/// PoracleNG's in-memory mute store, over <c>/api/v2/humans/{id}/mutes</c>.
/// </summary>
/// <remarks>
/// The first /api/v2 call in PoracleWeb.NET. v2 is huma-generated: its error envelope is
/// <c>{title,status,detail}</c>, not v1's <c>{error}</c>, and it exists only from PoracleNG 5.2.0 --
/// callers must ask <see cref="IMuteCapabilityService"/> first.
/// </remarks>
public interface IPoracleMuteProxy
{
    /// <summary>Active mutes for the human. Empty when there are none or the human is unknown.</summary>
    Task<IReadOnlyList<Mute>> ListAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates or extends a mute. <c>Replaced</c> is true when an existing mute for the same scope and
    /// value was extended rather than a new one made -- worth saying out loud, or the control reads as
    /// though it might have made a duplicate.
    /// </summary>
    /// <exception cref="MuteRejectedException">Upstream refused the scope/value pair (422).</exception>
    Task<(Mute Mute, bool Replaced)> CreateAsync(
        string userId, string scope, string? value, int durationMinutes, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes one mute, addressed by scope and value. An already-lapsed mute (404 upstream) returns an
    /// empty list rather than throwing: the caller asked for it to be gone and it is.
    /// </summary>
    Task<IReadOnlyList<Mute>> DeleteAsync(
        string userId, string scope, string? value, CancellationToken cancellationToken = default);

    /// <summary>Removes every mute the human has. Returns what was removed.</summary>
    Task<IReadOnlyList<Mute>> DeleteAllAsync(string userId, CancellationToken cancellationToken = default);
}
