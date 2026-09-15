using Pgan.PoracleWebNet.Core.Abstractions.Services;

namespace Pgan.PoracleWebNet.Core.Services;

/// <summary>
/// Whether the upstream PoracleNG exposes the v2 mute endpoints, answered from its reported version.
/// </summary>
/// <remarks>
/// <para>
/// Version, not the <c>/health</c> capability map. 5.2.1's map is
/// <c>{buttons, snapshots, autocreate, tomlDts, buttonResponseObject, derivedDtsTypes}</c> -- verified
/// against the live instance -- and carries no mutes key. Anyone "improving" this to consult
/// <c>Supports("mutes")</c> switches the whole feature off, because absent means false by that map's
/// own contract.
/// </para>
/// <para>
/// Fails closed, matching <see cref="SummaryCapabilityService"/>: unreachable, unparseable, or a
/// locally built binary reporting 0.0.0, all answer false. No cache of its own --
/// <see cref="IPoracleServerProfileService"/> already caches the profile for five minutes.
/// </para>
/// </remarks>
public class MuteCapabilityService(IPoracleServerProfileService serverProfile) : IMuteCapabilityService
{
    /// <summary>The release the v2 mute endpoints arrive in.</summary>
    public static readonly Version MinimumVersion = new(5, 2, 0);

    private readonly IPoracleServerProfileService _serverProfile = serverProfile;

    public async Task<bool> IsMuteApiAvailableAsync(CancellationToken cancellationToken = default)
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
