using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Api.Services;

/// <summary>
/// Says once, at startup, which PoracleNG this is talking to — and complains if it is too old.
/// </summary>
/// <remarks>
/// <para>
/// It complains and carries on. Refusing to start would take the whole site down over something an
/// operator can fix in minutes, and most of the site still works against an older PoracleNG; what does
/// not fails in ways that look like a bug in PoracleWeb.NET unless somebody says the version out loud.
/// So this does, on every boot, and the Versions card on the admin settings page says it again.
/// </para>
/// <para>
/// An unparseable version, or the <c>0.0.0</c> a locally built binary reports, is assumed current and
/// logged as such rather than complained about.
/// </para>
/// </remarks>
public partial class PoracleCompatibilityStartupService(
    IServiceScopeFactory scopeFactory,
    ILogger<PoracleCompatibilityStartupService> logger) : BackgroundService
{
    /// <summary>How long to wait after startup before checking. Production keeps the default; tests set zero.</summary>
    public TimeSpan StartupDelay { get; init; } = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // After the migration service, whose delay this matches; nothing here blocks startup.
        await Task.Delay(this.StartupDelay, stoppingToken);

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

            if (profile.ParsedVersion is null)
            {
                LogVersionUnknown(logger, profile.Version ?? "none", PoracleServerProfile.MinimumSupported.ToString());
            }

            LogConnected(
                logger,
                profile.Version ?? "unknown",
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
        Message = "Connected to PoracleNG {Version}. Capabilities: {Capabilities}.")]
    private static partial void LogConnected(ILogger logger, string version, string capabilities);

    [LoggerMessage(
        EventId = 6111,
        Level = LogLevel.Error,
        Message = "PoracleNG is {Version}, which this build of PoracleWeb.NET does not support: it needs {Minimum} or "
            + "newer. PoracleWeb.NET will keep running, but it calls PoracleNG routes that older "
            + "releases do not have, so alerts, profiles, quiet periods and Pokestop Events can fail with a server error "
            + "or be hidden. Upgrade PoracleNG.")]
    private static partial void LogTooOld(ILogger logger, string version, string minimum);

    [LoggerMessage(
        EventId = 6114,
        Level = LogLevel.Warning,
        Message = "PoracleNG reported version '{Version}', which is not a release number (a locally built binary reports "
            + "0.0.0). PoracleWeb.NET is assuming it is {Minimum} or newer; if it is older, upgrade it.")]
    private static partial void LogVersionUnknown(ILogger logger, string version, string minimum);

    [LoggerMessage(
        EventId = 6112,
        Level = LogLevel.Warning,
        Message = "PoracleNG did not answer its health endpoint, so its version is unknown. Alarm, human and profile "
            + "operations all proxy through it and will fail until it does.")]
    private static partial void LogUnreachable(ILogger logger);

    [LoggerMessage(EventId = 6113, Level = LogLevel.Debug, Message = "The PoracleNG compatibility check did not run.")]
    private static partial void LogCheckFailed(ILogger logger, Exception exception);
}
