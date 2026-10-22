using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models.Helpers;

namespace Pgan.PoracleWebNet.Api.Services;

/// <summary>
/// Gives a profile that was just created, duplicated or imported the areas and location it should have,
/// and removes the profile again when that fails.
/// </summary>
/// <remarks>
/// PoracleNG's add ignores <c>area</c>, <c>latitude</c> and <c>longitude</c>, so a new profile comes up
/// carrying whatever the active profile had (#466, #563). This sets them on that one profile through
/// <c>/api/v2</c>. There is no direct <c>profiles</c> write behind it, so a failure that is not just a
/// rejected area name would leave a profile with the wrong map: <see cref="WriteOrRollBackAsync"/> deletes
/// the just-created profile and surfaces the original error instead.
/// </remarks>
public static partial class NewProfileGeography
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

    /// <summary><see cref="WriteAsync"/>, deleting the new profile and rethrowing when it fails.</summary>
    public static async Task WriteOrRollBackAsync(
        IPoracleHumanProxy humanProxy,
        ILogger logger,
        string userId,
        int profileNo,
        string? areaJson,
        double latitude,
        double longitude)
    {
        try
        {
            await WriteAsync(humanProxy, userId, profileNo, areaJson, latitude, longitude);
        }
        catch
        {
            await RollBackAsync(humanProxy, logger, userId, profileNo);
            throw;
        }
    }

    /// <summary>
    /// Deletes a profile this request just created. A failure to delete is logged and swallowed so the
    /// error that caused the rollback is the one the caller sees.
    /// </summary>
    public static async Task RollBackAsync(IPoracleHumanProxy humanProxy, ILogger logger, string userId, int profileNo)
    {
        try
        {
            await humanProxy.DeleteProfileAsync(userId, profileNo);
        }
        catch (Exception ex)
        {
            LogRollbackFailed(logger, ex, profileNo);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not delete profile {ProfileNo} after its creation failed; it may be left behind")]
    private static partial void LogRollbackFailed(ILogger logger, Exception ex, int profileNo);
}
