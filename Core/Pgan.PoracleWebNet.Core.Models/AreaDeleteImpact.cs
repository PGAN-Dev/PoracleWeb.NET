namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>
/// What deleting a fence would do to the owner's alarms: the rules whose only override area is the fence,
/// which would otherwise lose the override and match across the profile's areas instead.
/// </summary>
/// <param name="WidenedByType">
/// Count of such rules per PoracleNG tracking type name (<c>pokemon</c>, <c>raid</c>, ... ; incident rules
/// count under <c>invasion</c>). Types with none are absent.
/// </param>
/// <param name="Total">The sum of <paramref name="WidenedByType"/>.</param>
public sealed record AreaDeleteImpact(IReadOnlyDictionary<string, int> WidenedByType, int Total);
