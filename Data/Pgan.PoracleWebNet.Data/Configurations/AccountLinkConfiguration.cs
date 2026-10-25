using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pgan.PoracleWebNet.Data.Entities;

namespace Pgan.PoracleWebNet.Data.Configurations;

public class AccountLinkConfiguration : IEntityTypeConfiguration<AccountLinkEntity>
{
    public void Configure(EntityTypeBuilder<AccountLinkEntity> builder)
    {
        // A human is in at most one link, and a link holds one Discord and one Telegram id.
        builder.Property(e => e.DiscordId)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(e => e.TelegramId)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(e => e.DefaultDestination)
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(e => e.CreatedAt)
            .IsRequired();

        builder.HasIndex(e => e.DiscordId)
            .IsUnique();

        builder.HasIndex(e => e.TelegramId)
            .IsUnique();
    }
}
