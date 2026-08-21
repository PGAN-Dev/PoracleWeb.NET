using Microsoft.Extensions.Logging;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

public sealed partial class FeatureGate(
    ISiteSettingService siteSettings,
    IUpstreamFeatureFlagService upstreamFlags,
    ILogger<FeatureGate> logger) : IFeatureGate
{
    private readonly ISiteSettingService _siteSettings = siteSettings;
    private readonly IUpstreamFeatureFlagService _upstreamFlags = upstreamFlags;
    private readonly ILogger<FeatureGate> _logger = logger;

    public async Task<bool> IsEnabledAsync(string disableKey) => !await this.IsDisabledAsync(disableKey);

    /// <summary>
    /// A feature is off if <em>either</em> source says so: the local <c>disable_*</c> site setting, or
    /// Poracle's own config. Poracle's flags are a floor, not a replacement — its processor already
    /// drops the webhook and its bot already refuses the command, so a type it has switched off can
    /// never fire, and offering it here only produces alarms that save and then do nothing (#769).
    /// The site setting is checked first because it is the cheaper of the two and the one an operator
    /// sets deliberately.
    /// </summary>
    private async Task<bool> IsDisabledAsync(string disableKey)
    {
        if (await this._siteSettings.GetBoolAsync(disableKey))
        {
            return true;
        }

        var upstreamDisabled = await this._upstreamFlags.GetDisabledKeysAsync();
        return upstreamDisabled.Contains(disableKey);
    }

    public async Task EnsureEnabledAsync(string disableKey)
    {
        if (await this.IsDisabledAsync(disableKey))
        {
            // Audit trail: a service-layer caller hit a disabled feature. Either a controller
            // path didn't have the [RequireFeatureEnabled] attribute, or a service-to-service
            // caller (QuickPick, profile import/duplicate, cleaning) routed past it. Either way
            // worth knowing for #236 follow-ups.
            LogFeatureDisabledThrow(this._logger, disableKey);
            throw new FeatureDisabledException(disableKey);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Service-layer feature gate blocked '{DisableKey}'")]
    private static partial void LogFeatureDisabledThrow(ILogger logger, string disableKey);
}
