using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Helpers;

/// <summary>Builds writers and links for tests: Discord <c>100000000000000001</c>, Telegram <c>200000001</c>.</summary>
internal static class PairTestSupport
{
    public const string DiscordId = "100000000000000001";
    public const string TelegramId = "200000001";

    public static Mock<ILinkedAccount> Unlinked()
    {
        var linked = new Mock<ILinkedAccount>();
        linked.Setup(l => l.ResolveAsync(It.IsAny<string>())).ReturnsAsync((string id) => LinkedPair.Single(id));
        return linked;
    }

    public static LinkedPair Pair(string callerId, bool discordExists = true, bool telegramExists = true,
        LinkSide defaultSide = LinkSide.Discord)
    {
        var link = new AccountLink(1, DiscordId, TelegramId, defaultSide, null, null, DateTime.UtcNow);
        return new LinkedPair(callerId, link,
            new PairSide(LinkSide.Discord, DiscordId, discordExists, discordExists ? PauseReason.None : PauseReason.NotRegistered),
            new PairSide(LinkSide.Telegram, TelegramId, telegramExists, telegramExists ? PauseReason.None : PauseReason.NotRegistered));
    }

    public static Mock<ILinkedAccount> Linked(LinkedPair pair)
    {
        var linked = new Mock<ILinkedAccount>();
        linked.Setup(l => l.ResolveAsync(It.IsAny<string>())).ReturnsAsync(pair);
        return linked;
    }

    public static PairedHumanWriter Writer(Mock<ILinkedAccount> linked) =>
        new(linked.Object, NullLogger<PairedHumanWriter>.Instance);

    /// <summary>A writer for a person with no link: one call, today's.</summary>
    public static PairedHumanWriter UnlinkedWriter() => Writer(Unlinked());
}
