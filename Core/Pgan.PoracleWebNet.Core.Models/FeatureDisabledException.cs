namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>
/// Thrown by alarm services when an operation is attempted against a feature whose
/// <c>disable_*</c> site setting is true. The web layer maps this to HTTP 403 via
/// <c>FeatureDisabledExceptionFilter</c>. See #236.
/// </summary>
public sealed class FeatureDisabledException : Exception
{
    public FeatureDisabledException(string disableKey)
        : base($"Feature '{disableKey}' is disabled by the administrator.") => this.DisableKey = disableKey;

    public FeatureDisabledException() : base("Feature is disabled by the administrator.") => this.DisableKey = string.Empty;

    public FeatureDisabledException(string disableKey, Exception innerException)
        : base($"Feature '{disableKey}' is disabled by the administrator.", innerException) => this.DisableKey = disableKey;

    public string DisableKey
    {
        get;
    }
}
