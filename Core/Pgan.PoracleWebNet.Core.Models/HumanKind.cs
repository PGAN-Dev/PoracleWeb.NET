namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>
/// Classifies a Poracle <c>humans.type</c> value as a person or a destination (a webhook, or a Discord or
/// Telegram channel, group, thread or topic). Follows upstream's <c>!broadcast</c> split, which treats any
/// <c>type LIKE '%:user'</c> as a person and everything else as a destination, so an operator's typo or a
/// type added later lands on the destinations side rather than on neither page. See #1025.
/// </summary>
public static class HumanKind
{
    /// <summary>A Discord webhook.</summary>
    public const string Webhook = "webhook";

    /// <summary>A Discord text channel.</summary>
    public const string DiscordChannel = "discord:channel";

    /// <summary>A Discord thread.</summary>
    public const string DiscordThread = "discord:thread";

    /// <summary>A Telegram channel.</summary>
    public const string TelegramChannel = "telegram:channel";

    /// <summary>A Telegram group.</summary>
    public const string TelegramGroup = "telegram:group";

    /// <summary>A Telegram forum topic.</summary>
    public const string TelegramTopic = "telegram:topic";

    /// <summary>
    /// The six types that may be created, granted and impersonated as destinations. This is the only list
    /// that grants power; an unknown non-user type is listed as a destination but is on none of the powers.
    /// </summary>
    public static IReadOnlySet<string> Destinations { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Webhook, DiscordChannel, DiscordThread, TelegramChannel, TelegramGroup, TelegramTopic,
    };

    /// <summary>Destinations that PoracleNG resolves by name, so a grant may name them rather than give an id.</summary>
    public static IReadOnlySet<string> NameAddressable { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Webhook, DiscordChannel, TelegramChannel, TelegramGroup,
    };

    /// <summary>Destinations whose messages can carry a role or user mention.</summary>
    public static IReadOnlySet<string> MentionCapable { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Webhook, DiscordChannel, DiscordThread,
    };

    /// <summary>True for a null, empty or blank type (legacy rows) and for any type ending <c>:user</c>.</summary>
    public static bool IsPerson(string? type) =>
        string.IsNullOrWhiteSpace(type) || type.EndsWith(":user", StringComparison.OrdinalIgnoreCase);

    /// <summary>True only for one of the six types in <see cref="Destinations"/>.</summary>
    public static bool IsKnownDestination(string? type) => type is not null && Destinations.Contains(type);

    /// <summary>Returns <c>"person"</c> or <c>"destination"</c>.</summary>
    public static string KindOf(string? type) => IsPerson(type) ? "person" : "destination";
}
