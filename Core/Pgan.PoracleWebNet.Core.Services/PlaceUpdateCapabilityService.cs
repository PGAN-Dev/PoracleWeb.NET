using Pgan.PoracleWebNet.Core.Abstractions.Services;

namespace Pgan.PoracleWebNet.Core.Services;

/// <summary>
/// Whether the upstream PoracleNG can move a saved place.
/// </summary>
/// <remarks>
/// <para>
/// <c>PUT /api/v2/humans/{id}/locations/{label}</c> is the one operation in the v2 migration with no v1
/// equivalent, so it is the one thing that needs a gate rather than a fallback. It arrived in 5.2.0, so
/// every supported PoracleNG has it. Same shape as <see cref="MuteCapabilityService"/>: answers
/// <see cref="Pgan.PoracleWebNet.Core.Models.PoracleServerProfile.IsSupported"/>, no cache of its own
/// because <see cref="IPoracleServerProfileService"/> already caches the profile for five minutes.
/// </para>
/// <para>
/// Unreachable, or known to be older than the minimum, answers false, and the SPA then offers the
/// delete-and-re-add flow rather than an edit button that would 404.
/// </para>
/// </remarks>
public class PlaceUpdateCapabilityService(IPoracleServerProfileService serverProfile) : IPlaceUpdateCapabilityService
{
    private readonly IPoracleServerProfileService _serverProfile = serverProfile;

    public async Task<bool> IsPlaceUpdateAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var profile = await this._serverProfile.GetAsync(cancellationToken);

            return profile.IsSupported;
        }
        catch
        {
            return false;
        }
    }
}
