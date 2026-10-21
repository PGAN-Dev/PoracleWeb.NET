namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>The outcome of an Areas save.</summary>
/// <param name="Stored">The requested names PoracleNG kept.</param>
/// <param name="Dropped">The requested names PoracleNG reported as <c>rejected</c> (it has no fence for them).</param>
public sealed record AreaSaveResult(IReadOnlyList<string> Stored, IReadOnlyList<string> Dropped);
