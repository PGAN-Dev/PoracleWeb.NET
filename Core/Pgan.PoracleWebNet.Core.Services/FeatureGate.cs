using Microsoft.Extensions.Logging;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

public sealed partial class FeatureGate(ISiteSettingService siteSettings, ILogger<FeatureGate> logger) : IFeatureGate
{
    private readonly ISiteSettingService _siteSettings = siteSettings;
    private readonly ILogger<FeatureGate> _logger = logger;

    public async Task<bool> IsEnabledAsync(string disableKey) => !await this._siteSettings.GetBoolAsync(disableKey);

    public async Task EnsureEnabledAsync(string disableKey)
    {
        if (await this._siteSettings.GetBoolAsync(disableKey))
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
