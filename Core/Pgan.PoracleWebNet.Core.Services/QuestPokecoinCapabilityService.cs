using Pgan.PoracleWebNet.Core.Abstractions.Services;

namespace Pgan.PoracleWebNet.Core.Services;

/// <summary>
/// Whether the upstream PoracleNG accepts <c>reward_type: 8</c> on a quest rule, answered from its
/// reported version.
/// </summary>
/// <remarks>
/// <para>
/// The version string is the only signal available here, and this is the exception to the otherwise
/// preferable "gate on the applied migration number" rule. Pokecoins added no column and no config
/// flag: 5.2.0 widened PoracleNG's <c>validRewardTypes</c> allowlist and nothing else. The quest table
/// is unchanged, so <see cref="Pgan.PoracleWebNet.Core.Models.PoracleServerProfile.HasSchema"/> cannot
/// tell the two versions apart, and the <c>/health</c> capability map carries no key for it either --
/// 5.2.1's map is <c>{buttons, snapshots, autocreate, tomlDts, buttonResponseObject, derivedDtsTypes}</c>,
/// verified live. Consulting <c>Supports("pokecoins")</c> would switch the feature off permanently,
/// because absent means false by that map's own contract.
/// </para>
/// <para>
/// Verified by calling both dev servers: 5.1.0 answers 400 "Unrecognised reward_type value"; 5.2.1
/// stores the row and describes it as "50 or more pokecoins".
/// </para>
/// <para>
/// Fails closed, matching <see cref="SummaryCapabilityService"/>: unreachable, unparseable, or a
/// locally built binary reporting 0.0.0 all answer false. No cache of its own --
/// <see cref="IPoracleServerProfileService"/> already caches the profile for five minutes.
/// </para>
/// </remarks>
public class QuestPokecoinCapabilityService(IPoracleServerProfileService serverProfile) : IQuestPokecoinCapabilityService
{
    /// <summary>The release that widened <c>validRewardTypes</c> to include pokecoins.</summary>
    public static readonly Version MinimumVersion = new(5, 2, 0);

    private readonly IPoracleServerProfileService _serverProfile = serverProfile;

    public async Task<bool> ArePokecoinRewardsSupportedAsync(CancellationToken cancellationToken = default)
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
