using Pgan.PoracleWebNet.Core.Abstractions.Services;

namespace Pgan.PoracleWebNet.Core.Services;

/// <summary>
/// Whether the upstream PoracleNG can move a saved place, answered from its reported version.
/// </summary>
/// <remarks>
/// <para>
/// <c>PUT /api/v2/humans/{id}/locations/{label}</c> is the one operation in the v2 migration with no v1
/// equivalent, so it is the one thing that needs a gate rather than a fallback. Same shape as
/// <see cref="MuteCapabilityService"/>: a version compare against 5.2.0, no cache of its own because
/// <see cref="IPoracleServerProfileService"/> already caches the profile for five minutes.
/// </para>
/// <para>
/// Fails closed. Unreachable, unparseable, or a locally built binary reporting 0.0.0 all answer false,
/// and the SPA then offers the delete-and-re-add flow it has always had rather than an edit button that
/// would 404.
/// </para>
/// </remarks>
public class PlaceUpdateCapabilityService(IPoracleServerProfileService serverProfile) : IPlaceUpdateCapabilityService
{
    /// <summary>The release the v2 saved-location routes arrive in.</summary>
    public static readonly Version MinimumVersion = new(5, 2, 0);

    private readonly IPoracleServerProfileService _serverProfile = serverProfile;

    public async Task<bool> IsPlaceUpdateAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var profile = await this._serverProfile.GetAsync(cancellationToken);

            return profile.Reachable && profile.ParsedVersion is { } version && version >= MinimumVersion;
        }
        catch
        {
            return false;
        }
    }
}
