using Pgan.PoracleWebNet.Core.Abstractions.Services;

namespace Pgan.PoracleWebNet.Api.Services;

/// <summary>
/// Background service that runs once on startup to migrate data from the old
/// pweb_settings KV table to the new structured tables in the PoracleWeb database.
/// </summary>
public partial class SettingsMigrationStartupService(
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
            LogMigrationCompleted(logger);
        }
        catch (Exception ex)
        {
            LogMigrationFailed(logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Settings migration completed successfully.")]
    private static partial void LogMigrationCompleted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Settings migration failed. The application will continue with existing data.")]
    private static partial void LogMigrationFailed(ILogger logger, Exception ex);
}
