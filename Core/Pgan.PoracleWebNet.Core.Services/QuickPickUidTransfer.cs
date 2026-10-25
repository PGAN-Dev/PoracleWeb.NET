using Microsoft.Extensions.Logging;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

/// <summary>
/// Follows a moved alarm in quick-pick applied state. A pick is applied per human and profile, so when an
/// alarm leaves one human for the other its uid has to leave the source's row and join the same pick's row
/// on the target, or removing the pick later would leave the moved alarm behind.
/// </summary>
public partial class QuickPickUidTransfer(
    IQuickPickAppliedStateRepository appliedStates,
    ILogger<QuickPickUidTransfer> logger)
{
    private readonly IQuickPickAppliedStateRepository _appliedStates = appliedStates;
    private readonly ILogger<QuickPickUidTransfer> _logger = logger;

    /// <param name="trackingType">PoracleNG's type name; <c>pokemon</c> is stored by quick picks as <c>monster</c>.</param>
    public async Task TransferAsync(string fromUserId, string toUserId, int profileNo, string trackingType, int oldUid, int newUid)
    {
        if (oldUid <= 0 || newUid <= 0 || string.Equals(fromUserId, toUserId, StringComparison.Ordinal))
        {
            return;
        }

        // A uid is unique only within a tracking type, so the type guard is what keeps a raid's uid 10 from
        // being taken for a pokemon's.
        var quickPickType = QuickPickAlarmType(trackingType);
        var sourceStates = await this._appliedStates.GetByUserAndProfileAsync(fromUserId, profileNo);

        foreach (var source in sourceStates)
        {
            if (!string.Equals(source.AlarmType, quickPickType, StringComparison.Ordinal) || !source.TrackedUids.Contains(oldUid))
            {
                continue;
            }

            // The target first: a failure between the two writes leaves the uid in both rows, never in neither.
            var target = await this._appliedStates.GetAsync(toUserId, profileNo, source.QuickPickId)
                ?? new QuickPickAppliedState
                {
                    UserId = toUserId,
                    ProfileNo = profileNo,
                    QuickPickId = source.QuickPickId,
                    AlarmType = source.AlarmType,
                    ExcludePokemonIds = [.. source.ExcludePokemonIds],
                };

            if (!target.TrackedUids.Contains(newUid))
            {
                target.TrackedUids.Add(newUid);
            }

            await this._appliedStates.CreateOrUpdateAsync(target);

            // A row tracking nothing would still list the pick as applied, so the last uid out takes the row with it.
            source.TrackedUids.RemoveAll(u => u == oldUid);
            if (source.TrackedUids.Count == 0)
            {
                await this._appliedStates.DeleteAsync(source.UserId, source.ProfileNo, source.QuickPickId);
            }
            else
            {
                await this._appliedStates.CreateOrUpdateAsync(source);
            }

            LogTransferred(this._logger, source.QuickPickId, trackingType, oldUid, newUid);
        }
    }

    // Same translation TrackedUidRemapper applies: pokemon is "pokemon" to PoracleNG and "monster" to a quick pick.
    private static string QuickPickAlarmType(string alarmType) =>
        string.Equals(alarmType, "pokemon", StringComparison.Ordinal) ? "monster" : alarmType;

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Quick pick {QuickPickId} now tracks moved {AlarmType} uid {NewUid} in place of {OldUid}.")]
    private static partial void LogTransferred(ILogger logger, string quickPickId, string alarmType, int oldUid, int newUid);
}
