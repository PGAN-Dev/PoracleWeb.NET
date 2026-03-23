using Pgan.PoracleWebNet.Core.Abstractions.Services;

namespace Pgan.PoracleWebNet.Api.Services;

/// <summary>
/// Background service that runs once on startup to migrate data from the old
/// pweb_settings KV table to the new structured tables in the PoracleWeb database.
/// </summary>
public class SettingsMigrationStartupService(
    IServiceScopeFactory scopeFactory,
    ILogger<SettingsMigrationStartupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Small delay to let the app finish starting
        await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);

        try
        {
            using var scope = scopeFactory.CreateScope();
            var migrationService = scope.ServiceProvider.GetRequiredService<ISettingsMigrationService>();
            await migrationService.MigrateAsync();
            logger.LogInformation("Settings migration completed successfully.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Settings migration failed. The application will continue with existing data.");
        }
    }
}
