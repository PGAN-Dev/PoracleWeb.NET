namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>
/// Thrown when a request needs a PoracleNG feature the server it is pointed at does not have.
/// </summary>
/// <remarks>
/// <para>
/// Distinct from <see cref="FeatureDisabledException"/>, which means an administrator switched
/// something off. This means nobody switched anything off: the request is reasonable, and the server
/// on the other end is simply an older PoracleNG. The two need different answers because the SPA
/// treats them differently — a disabled feature bounces the user to the dashboard, an unsupported one
/// explains what the server would need.
/// </para>
/// <para>
/// Thrown from the service layer rather than a controller filter so the paths that reach a service
/// without passing a controller action are covered too: quick-pick apply, profile duplicate and
/// profile import all fan out across the alarm services directly. That is the #565 shape.
/// </para>
/// </remarks>
public sealed class PoracleUnsupportedException : Exception
{
    public PoracleUnsupportedException(string capability, string requires)
        : base($"This PoracleNG does not support '{capability}'. It requires {requires}.")
    {
        this.Capability = capability;
        this.Requires = requires;
    }

    public PoracleUnsupportedException() : base("This PoracleNG does not support the requested feature.")
    {
        this.Capability = string.Empty;
        this.Requires = string.Empty;
    }

    public PoracleUnsupportedException(string message, Exception innerException)
        : base(message, innerException)
    {
        this.Capability = string.Empty;
        this.Requires = string.Empty;
    }

    /// <summary>The <see cref="PoracleCapabilityKeys"/> value that was missing.</summary>
    public string Capability
    {
        get;
    }

    /// <summary>What the server would need, in words, e.g. <c>PoracleNG schema 6</c>.</summary>
    public string Requires
    {
        get;
    }
}
