namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

/// <summary>
/// Whether the PoracleNG this instance talks to can store pokecoin quest rewards (<c>reward_type: 8</c>).
/// </summary>
public interface IQuestPokecoinCapabilityService
{
    /// <summary>
    /// True when the server is 5.2.0 or newer. Never throws -- an unreachable or unparseable server
    /// answers false, so the tab stays hidden rather than offering a control whose every save would be
    /// refused with 400 "Unrecognised reward_type value".
    /// </summary>
    Task<bool> ArePokecoinRewardsSupportedAsync(CancellationToken cancellationToken = default);
}
