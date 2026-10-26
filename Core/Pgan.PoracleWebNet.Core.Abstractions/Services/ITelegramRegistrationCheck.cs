using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

/// <summary>Asks Telegram whether a person sits in a group, through the Bot API's <c>getChatMember</c>.</summary>
public interface ITelegramMembershipClient
{
    /// <summary>
    /// True for a member, administrator or creator, and for a restricted user unless Telegram says they are not in the chat.
    /// Every failure (the bot is not in the group, the user is unknown, the network, no token configured) is false, never an exception.
    /// </summary>
    public Task<bool> IsMemberAsync(long chatId, long userId, CancellationToken cancellationToken = default);
}

/// <summary>Mirrors the rule behind PoracleNG's <c>/poracle</c> on Telegram, for the <c>when-qualified</c> link mode.</summary>
public interface ITelegramRegistrationCheck
{
    /// <summary>Never throws: anything that stops the answer from being known is "not qualified".</summary>
    public Task<TelegramQualification> CheckAsync(string telegramId);
}
