using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

/// <summary>
/// Scans every human's stored rules for the ones PoracleNG 5.4.0's <c>/api/v2</c> reads differently, and
/// repairs them through v2. A singleton: one scan or apply per process, each on its own background task
/// and its own DI scope.
/// </summary>
public interface IRuleRepairJob
{
    /// <summary>The current state. Never blocks on a running job.</summary>
    public RuleRepairStatus GetStatus();

    /// <summary>Starts a scan. False when a scan or apply is already running. The scan never writes.</summary>
    public bool TryStartScan();

    /// <summary>
    /// Starts applying the last scan's repairs, each re-checked by its own dry run. False when a job is
    /// running, or when there is no finished scan to apply (the phase is neither Scanned nor Applied).
    /// </summary>
    public bool TryStartApply();
}
