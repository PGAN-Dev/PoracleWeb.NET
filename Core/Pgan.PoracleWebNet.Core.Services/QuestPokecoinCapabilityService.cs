using Pgan.PoracleWebNet.Core.Abstractions.Services;

namespace Pgan.PoracleWebNet.Core.Services;

/// <summary>
/// Whether the upstream PoracleNG accepts <c>reward_type: 8</c> on a quest rule.
/// </summary>
/// <remarks>
/// <para>
/// Pokecoins added no column and no config flag: 5.2.0 widened PoracleNG's <c>validRewardTypes</c>
/// allowlist and nothing else. Every supported PoracleNG has it, so this answers
/// <see cref="Pgan.PoracleWebNet.Core.Models.PoracleServerProfile.IsSupported"/>. The <c>/health</c>
/// capability map carries no key for it -- 5.2.1's map is
/// <c>{buttons, snapshots, autocreate, tomlDts, buttonResponseObject, derivedDtsTypes}</c>, verified
/// live -- so consulting <c>Supports("pokecoins")</c> would switch the feature off permanently, because
/// absent means false by that map's own contract.
/// </para>
/// <para>
/// Verified by calling both dev servers: 5.1.0 answers 400 "Unrecognised reward_type value"; 5.2.1
/// stores the row and describes it as "50 or more pokecoins".
/// </para>
/// <para>
/// Unreachable, or known to be older than the minimum, answers false. An unparseable version or a
/// locally built binary reporting 0.0.0 counts as current. No cache of its own --
/// <see cref="IPoracleServerProfileService"/> already caches the profile for five minutes.
/// </para>
/// </remarks>
public class QuestPokecoinCapabilityService(IPoracleServerProfileService serverProfile) : IQuestPokecoinCapabilityService
{
    private readonly IPoracleServerProfileService _serverProfile = serverProfile;

    public async Task<bool> ArePokecoinRewardsSupportedAsync(CancellationToken cancellationToken = default)
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
