namespace Pgan.PoracleWebNet.Api.Services.Oidc;

/// <summary>
/// Carries the state of an in-progress refresh rotation between
/// <see cref="IOidcSessionService.StartRotationAsync"/> and its completion. The presented row is
/// already revoked when this is returned; the caller must either
/// <see cref="IOidcSessionService.CompleteRotationAsync"/> (success) or
/// <see cref="IOidcSessionService.AbortRotationAsync"/> (provider/userinfo failure).
/// </summary>
public sealed class OidcRotationTicket
{
    public required string UserId { get; init; }
    public required string FamilyId { get; init; }
    public required DateTime FamilyIssuedAt { get; init; }

    /// <summary>The provider refresh token decrypted from the presented session, for the IdP refresh call.</summary>
    public required string DecryptedRefreshToken { get; init; }

    /// <summary>The new opaque token to hand back to the browser once the successor row is persisted.</summary>
    public required string NewOpaqueToken { get; init; }

    /// <summary>SHA-256 of <see cref="NewOpaqueToken"/> — the successor row's primary lookup key (internal).</summary>
    public required string NewTokenHash { get; init; }
}

/// <summary>
/// Server-side mechanics for OIDC refresh sessions: opaque-token issuance, encrypted storage of
/// the provider refresh token, atomic rotation, replay/family-revoke, and absolute-cap enforcement.
/// Provider-agnostic and orchestrated by <c>AuthController</c> (which owns userinfo re-validation
/// and role resolution). All "invalid" conditions throw <see cref="UnauthorizedAccessException"/>
/// with message <c>invalid_grant</c>.
/// </summary>
public interface IOidcSessionService
{
    /// <summary>
    /// Creates a new rotation family for a freshly authenticated user and returns the opaque token
    /// to embed in the login callback. The provider refresh token is encrypted at rest.
    /// </summary>
    Task<string> IssueAsync(string userId, string idpRefreshToken, string? ipAddress, string? userAgent);

    /// <summary>
    /// Validates the presented opaque token (active, not replayed, within the absolute cap), then
    /// atomically revokes it and reserves a successor. Throws <see cref="UnauthorizedAccessException"/>
    /// on replay (revoking the whole family), expiry, or cap. The caller then performs the IdP
    /// refresh + userinfo re-validation using <see cref="OidcRotationTicket.DecryptedRefreshToken"/>.
    /// </summary>
    Task<OidcRotationTicket> StartRotationAsync(string opaqueToken, string? ipAddress, string? userAgent);

    /// <summary>Persists the successor session row (encrypting the carried-forward provider refresh token).</summary>
    Task CompleteRotationAsync(OidcRotationTicket ticket, string newIdpRefreshToken);

    /// <summary>Revokes the whole family when the IdP refresh or userinfo re-validation fails mid-rotation.</summary>
    Task AbortRotationAsync(OidcRotationTicket ticket, string reason);

    /// <summary>Revokes the family the presented opaque token belongs to (logout). Safe on unknown tokens.</summary>
    Task RevokeAsync(string opaqueToken, string reason);

    /// <summary>Revokes every active session for a user (admin disable / logout-everywhere).</summary>
    Task RevokeAllForUserAsync(string userId, string reason);
}
