using Microsoft.Extensions.Logging;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Abstractions.Services;

namespace Pgan.PoracleWebNet.Core.Services;

/// <inheritdoc cref="ITrackedUidRemapper"/>
public partial class TrackedUidRemapper(
    IQuickPickAppliedStateRepository appliedStateRepository,
    ILogger<TrackedUidRemapper> logger) : ITrackedUidRemapper
{
    private readonly IQuickPickAppliedStateRepository _appliedStateRepository = appliedStateRepository;
    private readonly ILogger<TrackedUidRemapper> _logger = logger;

    public async Task RemapAsync(string userId, string alarmType, int oldUid, int newUid)
    {
        if (oldUid == newUid || oldUid <= 0 || newUid <= 0 || string.IsNullOrEmpty(userId))
        {
            return;
        }

        try
        {
            // Scanned across every profile: an alarm edit does not tell us which profile the row belongs
            // to, and a uid is unique per user anyway, so a match in any profile is the right one.
            var states = await this._appliedStateRepository.GetByUserAsync(userId);

            foreach (var state in states)
            {
                if (!string.Equals(state.AlarmType, alarmType, StringComparison.Ordinal))
                {
                    continue;
                }

                var index = state.TrackedUids.IndexOf(oldUid);
                if (index < 0)
                {
                    continue;
                }

                state.TrackedUids[index] = newUid;
                await this._appliedStateRepository.CreateOrUpdateAsync(state);
                LogRemapped(this._logger, state.QuickPickId, alarmType, oldUid, newUid);
            }
        }
        catch (Exception ex)
        {
            // The edit itself already succeeded. Losing the remap costs the user a working "remove"
            // button on one quick pick; failing the request would cost them the edit. Log and move on.
            LogRemapFailed(this._logger, ex, alarmType, oldUid, newUid);
        }
    }

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Quick pick {QuickPickId} now tracks {AlarmType} uid {NewUid} in place of rotated uid {OldUid}.")]
    private static partial void LogRemapped(ILogger logger, string quickPickId, string alarmType, int oldUid, int newUid);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Could not remap quick-pick tracked {AlarmType} uid {OldUid} to {NewUid}; removing that quick pick may leave the alarm behind.")]
    private static partial void LogRemapFailed(ILogger logger, Exception exception, string alarmType, int oldUid, int newUid);
}
