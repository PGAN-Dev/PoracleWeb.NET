using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pgan.PoracleWebNet.Data.Entities;

namespace Pgan.PoracleWebNet.Data.Configurations;

public class QuickPickAppliedStateConfiguration : IEntityTypeConfiguration<QuickPickAppliedStateEntity>
{
    public void Configure(EntityTypeBuilder<QuickPickAppliedStateEntity> builder)
    {
        builder.Property(e => e.UserId)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.ProfileNo)
            .IsRequired();

        builder.Property(e => e.QuickPickId)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(e => e.AlarmType)
            .HasMaxLength(20)
            .HasDefaultValue("monster")
            .IsRequired();

        builder.Property(e => e.AppliedAt)
            .IsRequired();

        builder.Property(e => e.ExcludePokemonIdsJson)
            .HasColumnType("json");

        builder.Property(e => e.TrackedUidsJson)
            .HasColumnType("json")
            .IsRequired();

        builder.HasIndex(e => new { e.UserId, e.ProfileNo, e.QuickPickId })
            .IsUnique();
    }
}
