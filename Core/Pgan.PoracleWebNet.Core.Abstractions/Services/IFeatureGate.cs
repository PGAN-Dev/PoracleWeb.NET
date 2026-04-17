using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

/// <summary>
/// Single check used by every layer that gates on <c>disable_*</c> site settings.
/// Concentrates the "is this feature on?" decision in one place so the controller
/// filter and the service-layer guards can never disagree (which is exactly the
/// UI/API mismatch class of bug #236 was about).
/// </summary>
public interface IFeatureGate
{
    /// <summary>
    /// Returns true when the feature is enabled (the <c>disable_*</c> site setting is unset or "false").
    /// </summary>
    Task<bool> IsEnabledAsync(string disableKey);

    /// <summary>
    /// Throws <see cref="FeatureDisabledException"/> when the feature is disabled.
    /// </summary>
    Task EnsureEnabledAsync(string disableKey);
}
