namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>
/// Domain view of a server-side OIDC refresh session (one link in a rotation family).
/// The opaque token itself is never stored — only its SHA-256 hash — and the provider refresh
/// token in <see cref="EncryptedRefreshToken"/> stays encrypted at rest.
/// </summary>
public class OidcSession
{
    public int Id
    {
        get; set;
    }

    public string SessionTokenHash { get; set; } = string.Empty;
    public string FamilyId { get; set; } = string.Empty;
    public DateTime FamilyIssuedAt
    {
        get; set;
    }

    public string UserId { get; set; } = string.Empty;
    public string EncryptedRefreshToken { get; set; } = string.Empty;
    public DateTime ExpiresAt
    {
        get; set;
    }

    public DateTime CreatedUtc
    {
        get; set;
    }

    public DateTime? RevokedAt
    {
        get; set;
    }

    public string? RevokedReason
    {
        get; set;
    }

    public string? ReplacedByHash
    {
        get; set;
    }

    public string? IpAddress
    {
        get; set;
    }

    public string? UserAgent
    {
        get; set;
    }
}
