using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Abstractions.Repositories;

/// <summary>
/// Persistence for server-side OIDC refresh sessions (rotation families). All bulk revoke/cleanup
/// methods commit immediately: the revokes via EF Core's set-based <c>ExecuteUpdateAsync</c>, the
/// cleanup via raw SQL because the aliased delete EF generates is invalid on MariaDB (see #707).
/// </summary>
public interface IOidcSessionRepository
{
    /// <summary>Loads a session by the SHA-256 hash of the presented opaque token (no tracking).</summary>
    public Task<OidcSession?> GetByHashAsync(string sessionTokenHash);

    /// <summary>Inserts a new session row (issuance or rotation successor).</summary>
    public Task AddAsync(OidcSession session);

    /// <summary>
    /// Atomic rotation guard: revokes the presented row only if it is currently active
    /// (not revoked, not expired), stamping <c>rotation</c> + the successor hash. Returns the
    /// number of rows affected — exactly 1 on success, 0 if it was already rotated/expired
    /// (which the caller classifies as replay/expiry).
    /// </summary>
    public Task<int> TryRevokeForRotationAsync(string sessionTokenHash, string newHash);

    /// <summary>Revokes every still-active row in a family (replay/logout/cap/provider revoke).</summary>
    public Task<int> RevokeFamilyAsync(string familyId, string reason);

    /// <summary>Revokes every still-active session for a user (admin disable / logout-everywhere).</summary>
    public Task<int> RevokeAllForUserAsync(string userId, string reason);

    /// <summary>
    /// Set-based delete of expired rows and revoked rows older than the retention window.
    /// Returns the number of rows removed.
    /// </summary>
    public Task<int> DeleteExpiredAndStaleAsync(TimeSpan revokedRetention);
}
