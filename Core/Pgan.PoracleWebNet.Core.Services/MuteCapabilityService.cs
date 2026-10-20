using Pgan.PoracleWebNet.Core.Abstractions.Services;

namespace Pgan.PoracleWebNet.Core.Services;

/// <summary>
/// Whether the upstream PoracleNG exposes the v2 mute endpoints.
/// </summary>
/// <remarks>
/// <para>
/// The endpoints arrived in 5.2.0, so every supported PoracleNG has them and this answers
/// <see cref="Pgan.PoracleWebNet.Core.Models.PoracleServerProfile.IsSupported"/>. It used to compare the
/// version against 5.2.0 itself; with 5.2.1 the minimum that compare said nothing the shared rule does not.
/// </para>
/// <para>
/// Not the <c>/health</c> capability map. 5.2.1's map is
/// <c>{buttons, snapshots, autocreate, tomlDts, buttonResponseObject, derivedDtsTypes}</c> -- verified
/// against the live instance -- and carries no mutes key. Anyone "improving" this to consult
/// <c>Supports("mutes")</c> switches the whole feature off, because absent means false by that map's
/// own contract.
/// </para>
/// <para>
/// Unreachable, or known to be older than the minimum, answers false. An unparseable version or a
/// locally built binary reporting 0.0.0 counts as current. No cache of its own --
/// <see cref="IPoracleServerProfileService"/> already caches the profile for five minutes.
/// </para>
/// </remarks>
public class MuteCapabilityService(IPoracleServerProfileService serverProfile) : IMuteCapabilityService
{
    private readonly IPoracleServerProfileService _serverProfile = serverProfile;

    public async Task<bool> IsMuteApiAvailableAsync(CancellationToken cancellationToken = default)
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
