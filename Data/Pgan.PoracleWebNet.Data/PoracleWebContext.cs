using Microsoft.EntityFrameworkCore;
using Pgan.PoracleWebNet.Data.Entities;

namespace Pgan.PoracleWebNet.Data;

public class PoracleWebContext(DbContextOptions<PoracleWebContext> options) : DbContext(options)
{
    public DbSet<UserGeofenceEntity> UserGeofences
    {
        get; set;
    }

    public DbSet<SiteSettingEntity> SiteSettings
    {
        get; set;
    }

    public DbSet<WebhookDelegateEntity> WebhookDelegates
    {
        get; set;
    }

    public DbSet<QuickPickDefinitionEntity> QuickPickDefinitions
    {
        get; set;
    }

    public DbSet<QuickPickAppliedStateEntity> QuickPickAppliedStates
    {
        get; set;
    }

    public DbSet<OidcSessionEntity> OidcSessions
    {
        get; set;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PoracleWebContext).Assembly);
    }
}
