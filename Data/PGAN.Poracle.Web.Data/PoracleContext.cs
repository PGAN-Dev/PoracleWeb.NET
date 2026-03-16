using Microsoft.EntityFrameworkCore;
using PGAN.Poracle.Web.Data.Entities;

namespace PGAN.Poracle.Web.Data;

public class PoracleContext : DbContext
{
    public PoracleContext(DbContextOptions<PoracleContext> options)
        : base(options)
    {
    }

    public DbSet<HumanEntity> Humans => Set<HumanEntity>();
    public DbSet<ProfileEntity> Profiles => Set<ProfileEntity>();
    public DbSet<MonsterEntity> Monsters => Set<MonsterEntity>();
    public DbSet<RaidEntity> Raids => Set<RaidEntity>();
    public DbSet<EggEntity> Eggs => Set<EggEntity>();
    public DbSet<QuestEntity> Quests => Set<QuestEntity>();
    public DbSet<InvasionEntity> Invasions => Set<InvasionEntity>();
    public DbSet<LureEntity> Lures => Set<LureEntity>();
    public DbSet<NestEntity> Nests => Set<NestEntity>();
    public DbSet<GymEntity> Gyms => Set<GymEntity>();
    public DbSet<PwebSettingEntity> PwebSettings => Set<PwebSettingEntity>();

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

        // Set default values for NOT NULL text columns across all alarm entities
        modelBuilder.Entity<MonsterEntity>().Property(e => e.Ping).HasDefaultValue("");
        modelBuilder.Entity<RaidEntity>().Property(e => e.Ping).HasDefaultValue("");
        modelBuilder.Entity<EggEntity>().Property(e => e.Ping).HasDefaultValue("");
        modelBuilder.Entity<QuestEntity>().Property(e => e.Ping).HasDefaultValue("");
        modelBuilder.Entity<InvasionEntity>().Property(e => e.Ping).HasDefaultValue("");
        modelBuilder.Entity<LureEntity>().Property(e => e.Ping).HasDefaultValue("");
        modelBuilder.Entity<NestEntity>().Property(e => e.Ping).HasDefaultValue("");
        modelBuilder.Entity<GymEntity>().Property(e => e.Ping).HasDefaultValue("");

        // Human -> Monsters relationship
        modelBuilder.Entity<MonsterEntity>()
            .HasOne(m => m.Human)
            .WithMany(h => h.Monsters)
            .HasForeignKey(m => m.Id);

        // Human -> Raids relationship
        modelBuilder.Entity<RaidEntity>()
            .HasOne(r => r.Human)
            .WithMany(h => h.Raids)
            .HasForeignKey(r => r.Id);

        // Human -> Eggs relationship
        modelBuilder.Entity<EggEntity>()
            .HasOne(e => e.Human)
            .WithMany(h => h.Eggs)
            .HasForeignKey(e => e.Id);

        // Human -> Quests relationship
        modelBuilder.Entity<QuestEntity>()
            .HasOne(q => q.Human)
            .WithMany(h => h.Quests)
            .HasForeignKey(q => q.Id);

        // Human -> Invasions relationship
        modelBuilder.Entity<InvasionEntity>()
            .HasOne(i => i.Human)
            .WithMany(h => h.Invasions)
            .HasForeignKey(i => i.Id);

        // Human -> Lures relationship
        modelBuilder.Entity<LureEntity>()
            .HasOne(l => l.Human)
            .WithMany(h => h.Lures)
            .HasForeignKey(l => l.Id);

        // Human -> Nests relationship
        modelBuilder.Entity<NestEntity>()
            .HasOne(n => n.Human)
            .WithMany(h => h.Nests)
            .HasForeignKey(n => n.Id);

        // Human -> Gyms relationship
        modelBuilder.Entity<GymEntity>()
            .HasOne(g => g.Human)
            .WithMany(h => h.Gyms)
            .HasForeignKey(g => g.Id);
    }
}
