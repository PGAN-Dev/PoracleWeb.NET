using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Pgan.PoracleWebNet.Data.Entities;

/// <summary>
/// A server-side OIDC refresh session for an SSO-authenticated user. One row = one link in a
/// rotation chain (a "family"). The browser holds only an opaque PoracleWeb token whose SHA-256
/// hash is <see cref="SessionTokenHash"/>; the real provider refresh token lives encrypted in
/// <see cref="EncryptedRefreshToken"/> and never leaves the server. Used only when OIDC refresh
/// consumption is enabled (<c>Oidc:UseRefreshTokens</c>); otherwise this table stays empty.
/// </summary>
[Table("oidc_sessions")]
public class OidcSessionEntity
{
    [Key]
    [Column("id")]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id
    {
        get; set;
    }

    /// <summary>SHA-256 (hex) of the opaque PoracleWeb refresh token handed to the browser. Unique.</summary>
    [Column("session_token_hash")]
    [Required]
    public string SessionTokenHash { get; set; } = string.Empty;

    /// <summary>Rotation-chain id: every refresh revokes the presented row and inserts a successor in the same family.</summary>
    [Column("family_id")]
    [Required]
    public string FamilyId { get; set; } = string.Empty;

    /// <summary>When the family (login session) began — denormalized absolute-cap anchor.</summary>
    [Column("family_issued_at")]
    public DateTime FamilyIssuedAt { get; set; }

    /// <summary>The Poracle human id (a Discord/Telegram id) this session authenticates.</summary>
    [Column("user_id")]
    [Required]
    public string UserId { get; set; } = string.Empty;

    /// <summary>The provider's refresh token, protected via DataProtection. Never returned to clients.</summary>
    [Column("encrypted_refresh_token")]
    [Required]
    public string EncryptedRefreshToken { get; set; } = string.Empty;

    [Column("expires_at")]
    public DateTime ExpiresAt { get; set; }

    [Column("created_utc")]
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    [Column("revoked_at")]
    public DateTime? RevokedAt
    {
        get; set;
    }

    /// <summary>rotation | logout | replay_detected | absolute_cap | provider_revoked | account_inactive | admin_disable</summary>
    [Column("revoked_reason")]
    public string? RevokedReason
    {
        get; set;
    }

    [Column("replaced_by_hash")]
    public string? ReplacedByHash
    {
        get; set;
    }

    [Column("ip_address")]
    public string? IpAddress
    {
        get; set;
    }

    [Column("user_agent")]
    public string? UserAgent
    {
        get; set;
    }
}
