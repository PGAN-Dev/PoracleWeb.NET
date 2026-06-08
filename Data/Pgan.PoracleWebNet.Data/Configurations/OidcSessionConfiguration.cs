using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pgan.PoracleWebNet.Data.Entities;

namespace Pgan.PoracleWebNet.Data.Configurations;

public class OidcSessionConfiguration : IEntityTypeConfiguration<OidcSessionEntity>
{
    public void Configure(EntityTypeBuilder<OidcSessionEntity> builder)
    {
        builder.Property(e => e.SessionTokenHash)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(e => e.FamilyId)
            .HasMaxLength(36)
            .IsRequired();

        builder.Property(e => e.FamilyIssuedAt)
            .IsRequired();

        builder.Property(e => e.UserId)
            .HasMaxLength(100)
            .IsRequired();

        // longtext — DataProtection ciphertext is variable length.
        builder.Property(e => e.EncryptedRefreshToken)
            .IsRequired();

        builder.Property(e => e.ExpiresAt).IsRequired();
        builder.Property(e => e.CreatedUtc).IsRequired();
        builder.Property(e => e.RevokedReason).HasMaxLength(32);
        builder.Property(e => e.ReplacedByHash).HasMaxLength(64);
        builder.Property(e => e.IpAddress).HasMaxLength(45);
        builder.Property(e => e.UserAgent).HasMaxLength(256);

        // Hot lookup + DB-level reuse guard.
        builder.HasIndex(e => e.SessionTokenHash).IsUnique();
        // Family revoke.
        builder.HasIndex(e => e.FamilyId);
        // Revoke-all-for-user (composite, left-to-right covering).
        builder.HasIndex(e => new { e.UserId, e.RevokedAt });
        // Cleanup compound predicate.
        builder.HasIndex(e => new { e.RevokedAt, e.ExpiresAt });
    }
}
