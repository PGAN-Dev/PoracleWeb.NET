using Microsoft.Extensions.Options;
using Pgan.PoracleWebNet.Api.Configuration;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;

namespace Pgan.PoracleWebNet.Api.Services.Oidc;

/// <summary>
/// Periodically deletes expired and long-revoked OIDC refresh sessions with a single set-based
/// delete. Only does work when refresh consumption is enabled; otherwise the table stays empty
/// and each pass is a cheap no-op. Runs every 6 hours; revoked rows are retained for the same
/// number of days as the session cap (for audit/replay-forensics) before deletion.
/// </summary>
public sealed partial class OidcSessionCleanupService(
    IServiceScopeFactory scopeFactory,
    IOptions<OidcSettings> oidcSettings,
    ILogger<OidcSessionCleanupService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    private readonly OidcSettings _settings = oidcSettings.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Small initial stagger so startup isn't contended.
        await Task.Delay(TimeSpan.FromSeconds(240), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<IOidcSessionRepository>();
                var retention = TimeSpan.FromDays(Math.Max(1, this._settings.RevokedRetentionDays));
                var deleted = await repo.DeleteExpiredAndStaleAsync(retention);
                if (deleted > 0)
                {
                    LogCleanup(logger, deleted);
                }
            }
            catch (Exception ex)
            {
                LogCleanupFailed(logger, ex);
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "OIDC session cleanup removed {Count} expired/stale rows.")]
    private static partial void LogCleanup(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "OIDC session cleanup failed; will retry next interval.")]
    private static partial void LogCleanupFailed(ILogger logger, Exception ex);
}
