using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pgan.PoracleWebNet.Data.Entities;

namespace Pgan.PoracleWebNet.Data.Configurations;

public class QuickPickDefinitionConfiguration : IEntityTypeConfiguration<QuickPickDefinitionEntity>
{
    public void Configure(EntityTypeBuilder<QuickPickDefinitionEntity> builder)
    {
        builder.Property(e => e.Id)
            .HasMaxLength(50);

        builder.Property(e => e.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(e => e.Description)
            .HasColumnType("text");

        builder.Property(e => e.Icon)
            .HasMaxLength(50)
            .HasDefaultValue("bolt")
            .IsRequired();

        builder.Property(e => e.Category)
            .HasMaxLength(50)
            .HasDefaultValue("Common")
            .IsRequired();

        builder.Property(e => e.AlarmType)
            .HasMaxLength(20)
            .HasDefaultValue("monster")
            .IsRequired();

        builder.Property(e => e.SortOrder)
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(e => e.Enabled)
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(e => e.Scope)
            .HasMaxLength(10)
            .HasDefaultValue("global")
            .IsRequired();

        builder.Property(e => e.OwnerUserId)
            .HasMaxLength(100);

        builder.Property(e => e.FiltersJson)
            .HasColumnType("json")
            .IsRequired();

        builder.Property(e => e.CreatedAt)
            .IsRequired();

        builder.Property(e => e.UpdatedAt)
            .IsRequired();

        // Index for GetAllGlobalAsync (scope='global') and GetByOwnerAsync (scope='user', owner_user_id=?)
        builder.HasIndex(e => new { e.Scope, e.OwnerUserId });
    }
}
