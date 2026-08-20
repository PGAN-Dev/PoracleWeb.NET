using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Api.Services;

/// <summary>
/// Says once, at startup, which PoracleNG this is talking to — and complains if it is too old.
/// </summary>
/// <remarks>
/// Every feature that needs 5.1.0 fails the same quiet way against an older server: the column does not
/// exist, PoracleNG's decoder drops the field, the write returns 200 and the filter does nothing. That
/// is indistinguishable from a bug in PoracleWeb unless somebody says the version out loud, so this
/// does, on every boot.
/// </remarks>
public partial class PoracleCompatibilityStartupService(
    IServiceScopeFactory scopeFactory,
    ILogger<PoracleCompatibilityStartupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // After the migration service, whose delay this matches; nothing here blocks startup.
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        try
        {
            using var scope = scopeFactory.CreateScope();
            var profiles = scope.ServiceProvider.GetRequiredService<IPoracleServerProfileService>();
            var profile = await profiles.GetAsync(stoppingToken);

            if (!profile.Reachable)
            {
                LogUnreachable(logger);
                return;
            }

            if (profile.IsBelowMinimum)
            {
                LogTooOld(logger, profile.Version ?? "unknown", PoracleServerProfile.MinimumSupported.ToString());
                return;
            }

            LogConnected(
                logger,
                profile.Version ?? "unknown",
                profile.SchemaVersion?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown",
                profile.Capabilities.Count == 0 ? "none reported" : string.Join(", ", profile.Capabilities.Where(c => c.Value).Select(c => c.Key)));
        }
        catch (Exception ex)
        {
            // Never take the app down over a diagnostic.
            LogCheckFailed(logger, ex);
        }
    }

    [LoggerMessage(
        EventId = 6110,
        Level = LogLevel.Information,
        Message = "Connected to PoracleNG {Version} (schema {SchemaVersion}). Capabilities: {Capabilities}.")]
    private static partial void LogConnected(ILogger logger, string version, string schemaVersion, string capabilities);

    [LoggerMessage(
        EventId = 6111,
        Level = LogLevel.Error,
        Message = "PoracleNG is {Version}, and this build of PoracleWeb needs {Minimum} or newer. Per-alarm delivery "
            + "scope, the PVP mega evolution filter and the minimum time filter write columns that do not exist on "
            + "{Version}: those controls will appear to save and change nothing. Upgrade PoracleNG.")]
    private static partial void LogTooOld(ILogger logger, string version, string minimum);

    [LoggerMessage(
        EventId = 6112,
        Level = LogLevel.Warning,
        Message = "PoracleNG did not answer its health endpoint, so its version is unknown. Alarm, human and profile "
            + "operations all proxy through it and will fail until it does.")]
    private static partial void LogUnreachable(ILogger logger);

    [LoggerMessage(EventId = 6113, Level = LogLevel.Debug, Message = "The PoracleNG compatibility check did not run.")]
    private static partial void LogCheckFailed(ILogger logger, Exception exception);
}
