using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Models.Helpers;

namespace Pgan.PoracleWebNet.Api.Services;

/// <summary>
/// Gives a profile that was just created, duplicated or imported the areas and location it should have.
/// </summary>
/// <remarks>
/// <para>
/// PoracleNG's add ignores <c>area</c>, <c>latitude</c> and <c>longitude</c>, so a new profile comes up
/// carrying whatever the active profile had (#466, #563). Where the server can set them on one profile
/// through <c>/api/v2</c> this does that; otherwise, or when that path declines, it writes the
/// <c>profiles</c> row directly as every caller always has. See #993.
/// </para>
/// <para>
/// Never fails the request, the same as the direct write it replaces: the profile already exists and its
/// alarms may already be copied, and a wrong area list is fixable from the Areas page while a failed
/// create is not. A v2 refusal or an unreachable PoracleNG falls through to the direct write, which writes
/// areas and location together, so a half-applied v2 write is overwritten rather than left behind.
/// </para>
/// </remarks>
public static partial class NewProfileGeography
{
    /// <param name="name">
    /// What the direct write stores as the name. PoracleNG's add already stored it on both surfaces, so the
    /// v2 path does not send it.
    /// </param>
    /// <param name="areaJson">The area list in the column's JSON-encoded form, as the callers hold it.</param>
    public static async Task WriteAsync(
        IPoracleHumanProxy humanProxy,
        IProfileRepository profileRepository,
        ILogger logger,
        string userId,
        int profileNo,
        string name,
        string? areaJson,
        double latitude,
        double longitude)
    {
        try
        {
            if (await humanProxy.SetProfileGeographyAsync(
                    userId, profileNo, AreaListJson.Parse(areaJson), latitude, longitude))
            {
                return;
            }
        }
        catch (Exception ex) when (ex is PoracleRequestRefusedException or HttpRequestException or TaskCanceledException)
        {
            LogV2GeographyFailed(logger, ex, profileNo);
        }

        try
        {
            await profileRepository.UpdateAsync(new Profile
            {
                Id = userId,
                ProfileNo = profileNo,
                Name = name,
                Area = areaJson ?? "[]",
                Latitude = latitude,
                Longitude = longitude,
            });
        }
        catch (InvalidOperationException ex)
        {
            // The row is not visible yet in some PoracleNG timings. The profile exists either way.
            LogGeographyWriteFailed(logger, ex, profileNo);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not set areas and location on profile {ProfileNo} through PoracleNG; writing them directly instead")]
    private static partial void LogV2GeographyFailed(ILogger logger, Exception ex, int profileNo);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not write areas and location onto profile {ProfileNo}; it may have inherited them from the active profile")]
    private static partial void LogGeographyWriteFailed(ILogger logger, Exception ex, int profileNo);
}
