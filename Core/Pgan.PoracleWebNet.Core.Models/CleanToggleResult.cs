namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>
/// What a clean toggle changed: how many rules PoracleNG updated or found already set, and, for a linked pair
/// whose second human refused, the warning the response carries as <c>X-Pair-Warning</c>.
/// </summary>
/// <param name="Updated">Rules PoracleNG updated or found already set.</param>
/// <param name="Warning">The linked human that did not take the toggle, if any.</param>
/// <param name="RefusedRules">
/// Rules PoracleNG left at their old setting (for a clean toggle, usually because the changed rule would collide
/// with another). Null is none.
/// </param>
public readonly record struct CleanToggleResult(int Updated, PairWarning? Warning = null, IReadOnlyList<PatchRefusal>? RefusedRules = null)
{
    /// <inheritdoc cref="CleanToggleResult" path="/param[@name='RefusedRules']"/>
    public IReadOnlyList<PatchRefusal> Refused => this.RefusedRules ?? [];
}
