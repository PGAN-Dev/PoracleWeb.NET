namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

/// <summary>
/// Whether the PoracleNG this instance talks to exposes mutes over HTTP at all.
/// </summary>
public interface IMuteCapabilityService
{
    /// <summary>
    /// True when the server is 5.2.0 or newer. Never throws -- an unreachable or unparseable server
    /// answers false, so the surface stays hidden rather than offering a control every press of which
    /// would 404.
    /// </summary>
    Task<bool> IsMuteApiAvailableAsync(CancellationToken cancellationToken = default);
}
