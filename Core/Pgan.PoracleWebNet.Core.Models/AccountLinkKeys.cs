namespace Pgan.PoracleWebNet.Core.Models;

public static class AccountLinkKeys
{
    /// <summary>PoracleNG human type of a Discord person. A link pairs this with <see cref="TelegramUserType"/>, never a destination type.</summary>
    public const string DiscordUserType = "discord:user";

    public const string TelegramUserType = "telegram:user";

    /// <summary>Site setting: whether to create the missing side's human when a link is made, as a <see cref="LinkRegisterMode"/> read by <see cref="LinkRegisterModes.Parse"/>. It was a boolean before it had three values: "true" reads as always, "false" or absent as off.</summary>
    public const string RegisterMissing = "link_register_missing";

    public static string Wire(LinkSide side) => side == LinkSide.Discord ? "discord" : "telegram";

    public static bool TryParse(string? value, out LinkSide side)
    {
        if (string.Equals(value, "discord", StringComparison.OrdinalIgnoreCase))
        {
            side = LinkSide.Discord;
            return true;
        }

        if (string.Equals(value, "telegram", StringComparison.OrdinalIgnoreCase))
        {
            side = LinkSide.Telegram;
            return true;
        }

        side = default;
        return false;
    }
}
