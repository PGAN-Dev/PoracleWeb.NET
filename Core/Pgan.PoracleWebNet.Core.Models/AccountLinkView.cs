namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>What the API says about a link: sides spelled <c>discord</c> / <c>telegram</c>, never an enum number.</summary>
public sealed record AccountLinkView(
    int Id,
    string DiscordId,
    string TelegramId,
    string DefaultDestination,
    DateTime? DiscordVerifiedAt,
    DateTime? TelegramVerifiedAt,
    DateTime CreatedAt)
{
    public static AccountLinkView From(AccountLink link) => new(
        link.Id,
        link.DiscordId,
        link.TelegramId,
        AccountLinkKeys.Wire(link.DefaultDestination),
        link.DiscordVerifiedAt,
        link.TelegramVerifiedAt,
        link.CreatedAt);
}
