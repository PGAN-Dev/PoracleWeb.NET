using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

/// <summary>
/// PoracleNG's linked game accounts (player ids) for a human, over <c>/api/v2/humans/{id}/accounts</c>.
/// </summary>
/// <remarks>
/// A server without the route answers 404 with gin's plaintext body, surfaced as a
/// <see cref="PoracleRequestRefusedException"/> with status 501. Player ids are case-sensitive and are
/// passed through untouched.
/// </remarks>
public interface IPoracleAccountProxy
{
    Task<IReadOnlyList<PlayerAccount>> ListAsync(string userId, CancellationToken ct = default);

    Task<PlayerAccount> AddAsync(string userId, string playerId, string? label, CancellationToken ct = default);

    /// <summary>Sets the label; an empty label clears it.</summary>
    Task RelabelAsync(string userId, string playerId, string label, CancellationToken ct = default);

    Task DeleteAsync(string userId, string playerId, CancellationToken ct = default);
}
