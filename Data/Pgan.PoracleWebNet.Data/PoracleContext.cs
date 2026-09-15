using Microsoft.EntityFrameworkCore;
using Pgan.PoracleWebNet.Data.Entities;

namespace Pgan.PoracleWebNet.Data;

/// <summary>
/// The Poracle database, read and written only where PoracleNG's API cannot serve the operation.
/// </summary>
/// <remarks>
/// There are deliberately no alarm entities here. Every tracking write goes through PoracleNG so that
/// its dedup, defaults and state reload run; the two places that still reach the alarm tables directly
/// (<c>IUserAreaDualWriter</c>) do so with raw SQL over a validated table name, not through EF. Mapping
/// the tables again would make a direct alarm write one <c>DbSet</c> away, which is the thing the 2.0
/// migration existed to prevent.
/// </remarks>
public class PoracleContext(DbContextOptions<PoracleContext> options) : DbContext(options)
{
    public DbSet<HumanEntity> Humans => this.Set<HumanEntity>();
    public DbSet<ProfileEntity> Profiles => this.Set<ProfileEntity>();
    public DbSet<PwebSettingEntity> PwebSettings => this.Set<PwebSettingEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ProfileEntity has a composite primary key
        modelBuilder.Entity<ProfileEntity>()
            .HasKey(p => new { p.Id, p.ProfileNo });

        // Human -> Profiles relationship
        modelBuilder.Entity<ProfileEntity>()
            .HasOne(p => p.Human)
            .WithMany(h => h.Profiles)
            .HasForeignKey(p => p.Id);

        // Ensure pweb_settings.value can hold JSON blobs (quick pick definitions, applied states)
        modelBuilder.Entity<PwebSettingEntity>().Property(e => e.Value).HasColumnType("longtext");
    }
}
