using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

/// <summary>
/// Answers which optional PoracleNG features the configured server actually has.
/// </summary>
/// <remarks>
/// PoracleNG ships a released <c>main</c> and a longer-running <c>develop</c>, and PoracleWeb supports
/// both. Everything present on <c>main</c> is assumed and never asked about; everything that exists
/// only on <c>develop</c> is asked about here, so a self-hoster on either branch gets a UI that matches
/// their server. See <see cref="PoracleCapabilityKeys"/> for the rules and why each uses the signal it does.
/// </remarks>
public interface IPoracleCapabilityService
{
    /// <summary>
    /// The <see cref="PoracleCapabilityKeys"/> values this server supports. Empty when PoracleNG is
    /// unreachable or too old for any of them, which the caller must not be able to tell apart: both
    /// mean "offer none of these".
    /// </summary>
    Task<IReadOnlySet<string>> GetSupportedAsync(CancellationToken cancellationToken = default);

    /// <summary>True when this server supports <paramref name="capability"/>.</summary>
    Task<bool> SupportsAsync(string capability, CancellationToken cancellationToken = default);

    /// <summary>
    /// Throws <see cref="PoracleUnsupportedException"/> unless this server supports
    /// <paramref name="capability"/>. The service-layer guard, mirroring
    /// <c>IFeatureGate.EnsureEnabledAsync</c>.
    /// </summary>
    Task EnsureSupportedAsync(string capability, CancellationToken cancellationToken = default);
}
