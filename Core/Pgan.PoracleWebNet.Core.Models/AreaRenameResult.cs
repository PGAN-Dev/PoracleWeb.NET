namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>What PoracleNG's <c>PATCH /api/v2/humans/{id}/areas</c> renamed, or would rename on a dry run.</summary>
/// <param name="RenamedIn">Profile numbers whose area list changed. Empty when nothing held the old name.</param>
/// <param name="DryRun">True when nothing was written.</param>
public sealed record AreaRenameResult(IReadOnlyList<int> RenamedIn, bool DryRun);
