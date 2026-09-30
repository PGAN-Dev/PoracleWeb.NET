namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>
/// What a bulk "Update Distance" did: how many alarms took the new radius, and which ones it left alone
/// because a radius cannot apply to their scope.
/// </summary>
/// <remarks>
/// PoracleNG refuses an alarm limited to areas that also carries a radius, and an alarm measured from a
/// saved place with none. Writing those rows failed the whole selection, so they are skipped and named
/// here for the SPA to report.
/// </remarks>
/// <param name="Updated">Alarms written with the new radius.</param>
/// <param name="SkippedAreaScoped">Selected alarms limited to areas, left alone because the radius was above zero.</param>
/// <param name="SkippedPlaceScoped">Selected alarms measured from a saved place, left alone because the radius was zero.</param>
public sealed record DistanceUpdateResult(
    int Updated,
    IReadOnlyList<int> SkippedAreaScoped,
    IReadOnlyList<int> SkippedPlaceScoped)
{
    /// <summary>Nothing selected, nothing skipped.</summary>
    public static DistanceUpdateResult None { get; } = new(0, [], []);
}
