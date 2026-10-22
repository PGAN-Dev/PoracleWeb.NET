using Pgan.PoracleWebNet.Core.Abstractions.Services;

namespace Pgan.PoracleWebNet.Api.Services;

/// <summary>
/// Background service that runs once on startup to seed default site settings and repair stored
/// ones in the PoracleWeb database.
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
            await migrationService.SeedDefaultsAsync();
            LogMigrationCompleted(logger);
        }
        catch (Exception ex)
        {
            LogMigrationFailed(logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Default settings seeding completed successfully.")]
    private static partial void LogMigrationCompleted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Default settings seeding failed. The application will continue with existing data.")]
    private static partial void LogMigrationFailed(ILogger logger, Exception ex);
}
