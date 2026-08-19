using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

/// <summary>
/// Compares what this deployment runs against what has been published.
/// </summary>
public interface IUpdateCheckService
{
    /// <summary>
    /// Whether either component is behind. Never throws and never blocks on the network for long: a
    /// failed or disabled check reports Unknown, which the UI renders as nothing rather than as news.
    /// </summary>
    Task<(UpdateStatus PoracleWeb, UpdateStatus PoracleNg)> CheckAsync(
        string? runningPoracleWeb,
        string? runningPoracleNg,
        CancellationToken cancellationToken = default);

    /// <summary>Drops the cached answer so the next check asks GitHub again.</summary>
    void Invalidate();
}
