using System.Globalization;
using Microsoft.Extensions.Logging;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

/// <summary>
/// The rule behind PoracleNG's <c>/poracle</c> (telegrambot/reconciliation.go, <c>reconcileNonAreaSecurity</c> and
/// <c>reconcileAreaSecurity</c>): without area security a member of any <c>telegram.channels</c> group is admitted;
/// with it, each community whose own <c>telegram.channels</c> holds the person is recorded, and having one is enough.
/// A channel that is not a number is skipped, as PoracleNG does.
/// </summary>
public partial class TelegramRegistrationCheck(
    IPoracleApiProxy poracle,
    ITelegramMembershipClient telegram,
    ILogger<TelegramRegistrationCheck> logger) : ITelegramRegistrationCheck
{
    private readonly ILogger<TelegramRegistrationCheck> _logger = logger;
    private readonly IPoracleApiProxy _poracle = poracle;
    private readonly ITelegramMembershipClient _telegram = telegram;

    public async Task<TelegramQualification> CheckAsync(string telegramId)
    {
        if (!long.TryParse(telegramId, NumberStyles.None, CultureInfo.InvariantCulture, out var userId) || userId <= 0)
        {
            return TelegramQualification.No;
        }

        TelegramRegistrationGroups? groups;
        try
        {
            groups = await this._poracle.GetTelegramRegistrationGroupsAsync();
        }
        catch (Exception ex)
        {
            LogGroupsUnreadable(this._logger, ex.GetType().Name);
            return TelegramQualification.No;
        }

        if (groups is null)
        {
            return TelegramQualification.No;
        }

        // One question per group however many communities list it.
        var answers = new Dictionary<long, bool>();

        async Task<bool> InAnyAsync(IEnumerable<string> channels)
        {
            foreach (var channel in channels)
            {
                if (!long.TryParse(channel, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var chatId))
                {
                    continue;
                }

                if (!answers.TryGetValue(chatId, out var member))
                {
                    try
                    {
                        member = await this._telegram.IsMemberAsync(chatId, userId);
                    }
                    catch (Exception ex)
                    {
                        LogGroupFailed(this._logger, ex.GetType().Name, chatId);
                        member = false;
                    }

                    answers[chatId] = member;
                }

                if (member)
                {
                    return true;
                }
            }

            return false;
        }

        if (!groups.AreaSecurity)
        {
            return await InAnyAsync(groups.Channels) ? new TelegramQualification(true, []) : TelegramQualification.No;
        }

        var communities = new List<string>();
        foreach (var community in groups.Communities)
        {
            if (await InAnyAsync(community.Channels))
            {
                communities.Add(community.Name);
            }
        }

        return communities.Count > 0 ? new TelegramQualification(true, communities) : TelegramQualification.No;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not read PoracleNG's Telegram registration groups ({ExceptionType}); the person is treated as not qualified")]
    private static partial void LogGroupsUnreadable(ILogger logger, string exceptionType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Checking Telegram group {ChatId} failed ({ExceptionType}); treating the person as not in it")]
    private static partial void LogGroupFailed(ILogger logger, string exceptionType, long chatId);
}
