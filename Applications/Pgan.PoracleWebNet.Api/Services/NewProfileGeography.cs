using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models.Helpers;

namespace Pgan.PoracleWebNet.Api.Services;

/// <summary>
/// Gives a profile that was just created, duplicated or imported the areas and location it should have.
/// </summary>
/// <remarks>
/// PoracleNG's add ignores <c>area</c>, <c>latitude</c> and <c>longitude</c>, so a new profile comes up
/// carrying whatever the active profile had (#466, #563). This sets them on that one profile through
/// <c>/api/v2</c>. A refusal propagates: there is no direct <c>profiles</c> write behind it any more, so a
/// failure is reported rather than papered over. The profile already exists when this runs; the caller
/// decides whether to remove it.
/// </remarks>
public static class NewProfileGeography
{
    /// <param name="areaJson">The area list in the column's JSON-encoded form, as the callers hold it.</param>
    public static Task WriteAsync(
        IPoracleHumanProxy humanProxy,
        string userId,
        int profileNo,
        string? areaJson,
        double latitude,
        double longitude) =>
        humanProxy.SetProfileGeographyAsync(userId, profileNo, AreaListJson.Parse(areaJson), latitude, longitude);
}
