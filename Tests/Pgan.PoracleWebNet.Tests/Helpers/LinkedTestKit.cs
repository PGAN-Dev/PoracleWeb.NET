using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Helpers;

/// <summary>Link fixtures shared by the tests of the surfaces that read or act across a linked pair.</summary>
internal static class LinkedTestKit
{
    public const string Discord = "100000000000000001";
    public const string Telegram = "200000001";

    /// <summary>What a person with no link gets: their own id and nothing read from PoracleNG.</summary>
    public static ILinkedAccount Unlinked()
    {
        var linked = new Mock<ILinkedAccount>();
        linked.Setup(l => l.ResolveAsync(It.IsAny<string>())).ReturnsAsync((string id) => LinkedPair.Single(id));
        return linked.Object;
    }

    /// <summary>A pair whose Discord and Telegram humans both exist, for whichever of the two asks.</summary>
    public static ILinkedAccount Linked(LinkSide def = LinkSide.Discord)
    {
        var linked = new Mock<ILinkedAccount>();
        linked.Setup(l => l.ResolveAsync(It.IsAny<string>())).ReturnsAsync((string caller) => new LinkedPair(
            caller,
            new AccountLink(1, Discord, Telegram, def, null, null, DateTime.UtcNow),
            new PairSide(LinkSide.Discord, Discord, true, PauseReason.None),
            new PairSide(LinkSide.Telegram, Telegram, true, PauseReason.None)));
        return linked.Object;
    }

    public static LinkedAlarmRouter Router(ILinkedAccount linked) =>
        new(linked, Controllers.ControllerTestBase.InertMover(linked));

    public static IHumanService Humans(int discordProfile = 1, int telegramProfile = 1)
    {
        var humans = new Mock<IHumanService>();
        humans.Setup(h => h.GetByIdAsync(Discord)).ReturnsAsync(new Human { Id = Discord, CurrentProfileNo = discordProfile });
        humans.Setup(h => h.GetByIdAsync(Telegram)).ReturnsAsync(new Human { Id = Telegram, CurrentProfileNo = telegramProfile });
        return humans.Object;
    }
}
