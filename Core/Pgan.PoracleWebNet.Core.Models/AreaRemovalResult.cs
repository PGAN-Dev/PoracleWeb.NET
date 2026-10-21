namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>
/// What PoracleNG's <c>DELETE /api/v2/humans/{id}/areas</c> removed, or would remove on a dry run.
/// </summary>
/// <param name="RemovedFrom">Profile numbers whose area list held the name.</param>
/// <param name="Broadened">
/// Rules whose only override area was the name, keyed by PoracleNG's tracking type name (incident rules
/// report as <c>invasion</c>). Their override is cleared, so they match across every area instead of one.
/// Empty when no rule was affected.
/// </param>
/// <param name="DryRun">True when nothing was written.</param>
public sealed record AreaRemovalResult(
    IReadOnlyList<int> RemovedFrom,
    IReadOnlyDictionary<string, IReadOnlyList<int>> Broadened,
    bool DryRun);
