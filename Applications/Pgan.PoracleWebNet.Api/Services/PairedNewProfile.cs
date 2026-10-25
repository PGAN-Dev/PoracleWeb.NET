using Microsoft.Extensions.Logging;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Api.Services;

/// <summary>A profile just created on one human of a pair.</summary>
public sealed record NewProfileOn(string HumanId, int No);

/// <summary>
/// Settles the numbers of a profile created on both humans of a linked pair. PoracleNG gives each human its
/// lowest free number, so two humans that had already drifted answer different ones.
/// </summary>
public static class PairedNewProfile
{
    public const string OutOfStepReason = "The two accounts' profiles don't line up.";

    /// <summary>
    /// The signed-in human's number, and a warning if the other human's profile took another number: that
    /// profile is deleted rather than left beside one it does not match, and the difference is the warning.
    /// </summary>
    public static async Task<(int No, PairWarning? Warning)> ReconcileAsync(
        IPairedHumanWriter writer,
        IPoracleHumanProxy humanProxy,
        ILogger logger,
        string userId,
        PairWriteResult<NewProfileOn> written)
    {
        if (written.Other is not { } other || other.No == written.Primary.No)
        {
            return (written.Primary.No, written.Warning);
        }

        await NewProfileGeography.RollBackAsync(humanProxy, logger, other.HumanId, other.No);
        var side = await writer.SideOfAsync(userId, other.HumanId) ?? LinkSide.Telegram;
        return (written.Primary.No, new PairWarning(side, OutOfStepReason, PairWarningCodes.OutOfStep));
    }
}
