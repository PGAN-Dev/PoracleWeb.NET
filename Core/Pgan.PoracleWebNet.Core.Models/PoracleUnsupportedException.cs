namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>
/// The request needs something the PoracleNG on the other end does not have.
/// </summary>
/// <remarks>
/// <para>
/// Distinct from <see cref="FeatureDisabledException"/>, which means an administrator switched
/// something off. Nobody switched anything off here: the request is reasonable and the server is
/// simply an older PoracleNG. Without this the failure arrives either as PoracleNG's own wording,
/// which names a column rather than a feature, or as a generic 500.
/// </para>
/// <para>
/// It carries a plain feature name and the version or schema it would need, not a registry key.
/// Support is decided per feature by <c>IPoracleServerProfileService</c> and the small services on
/// top of it, so there is nothing central to look a key up in — and the words are what the user
/// reads, so they belong at the throw site where somebody knows what the feature is called.
/// </para>
/// <para>
/// Thrown from the service layer rather than checked in a controller so the paths that reach a
/// service without passing a controller action are covered too: quick-pick apply, profile duplicate
/// and profile import all fan out across the alarm services directly. That is the #565 shape.
/// </para>
/// </remarks>
public sealed class PoracleUnsupportedException : Exception
{
    /// <param name="feature">What the user was trying to use, in their words, e.g. <c>costume filters</c>.</param>
    /// <param name="requires">What the server would need, in words, e.g. <c>PoracleNG 5.2.1 or newer</c>.</param>
    public PoracleUnsupportedException(string feature, string requires)
        : base($"This PoracleNG does not support {feature}. It requires {requires}.")
    {
        this.Feature = feature;
        this.Requires = requires;
    }

    public PoracleUnsupportedException() : base("This PoracleNG does not support the requested feature.")
    {
        this.Feature = string.Empty;
        this.Requires = string.Empty;
    }

    public PoracleUnsupportedException(string message) : base(message)
    {
        this.Feature = string.Empty;
        this.Requires = string.Empty;
    }

    public PoracleUnsupportedException(string message, Exception innerException)
        : base(message, innerException)
    {
        this.Feature = string.Empty;
        this.Requires = string.Empty;
    }

    /// <summary>The feature that is missing, named as the UI names it.</summary>
    public string Feature
    {
        get;
    }

    /// <summary>What the server would need, in words, e.g. <c>PoracleNG database migration 6</c>.</summary>
    public string Requires
    {
        get;
    }
}
