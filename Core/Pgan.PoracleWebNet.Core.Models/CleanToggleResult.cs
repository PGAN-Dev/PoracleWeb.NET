namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>
/// What a clean toggle changed: how many rules PoracleNG updated or found already set, and, for a linked pair
/// whose second human refused, the warning the response carries as <c>X-Pair-Warning</c>.
/// </summary>
public readonly record struct CleanToggleResult(int Updated, PairWarning? Warning = null);
