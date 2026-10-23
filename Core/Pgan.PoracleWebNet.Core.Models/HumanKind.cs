using System.Text.RegularExpressions;

namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>
/// Classifies a Poracle <c>humans.type</c> value as a person or a destination (a webhook, or a Discord or
/// Telegram channel, group, thread or topic). Follows upstream's <c>!broadcast</c> split, which treats any
/// <c>type LIKE '%:user'</c> as a person and everything else as a destination, so an operator's typo or a
/// type added later lands on the destinations side rather than on neither page. See #1025.
/// </summary>
public static partial class HumanKind
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

    /// <summary>
    /// Checks the id an admin gives for a new destination of <paramref name="type"/>, answering a sentence
    /// to show them, or null when the id could be real. Refuses only what cannot be: Discord snowflakes are
    /// digits, Telegram chats are negative (a supergroup's <c>-100…</c> and a legacy group's shorter id
    /// alike), a topic is its chat id and a positive thread id, and a webhook is an http(s) URL.
    /// </summary>
    public static string? ValidateNewId(string type, string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return "An id is required.";
        }

        if (id.Any(char.IsControl))
        {
            return "The id cannot contain control characters.";
        }

        switch (type?.ToLowerInvariant())
        {
            case Webhook:
                return id.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    || id.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                    ? null
                    : "A webhook must be an http:// or https:// URL.";
            case DiscordChannel:
            case DiscordThread:
                return DiscordId().IsMatch(id) ? null : "A Discord channel or thread id is a number of up to 20 digits.";
            case TelegramChannel:
            case TelegramGroup:
                return TelegramChatId().IsMatch(id) ? null : "A Telegram chat id is a negative number, such as -1001234567890.";
            case TelegramTopic:
                return TelegramTopicId().IsMatch(id)
                    ? null
                    : "A Telegram topic id is the chat id, a colon and the topic number, such as -1001234567890:42.";
            default:
                return "This type cannot be created as a destination.";
        }
    }

    [GeneratedRegex("^[0-9]{1,20}$", RegexOptions.CultureInvariant)]
    private static partial Regex DiscordId();

    [GeneratedRegex("^-[0-9]{1,19}$", RegexOptions.CultureInvariant)]
    private static partial Regex TelegramChatId();

    [GeneratedRegex("^(-[0-9]{1,19}):([1-9][0-9]{0,9})$", RegexOptions.CultureInvariant)]
    private static partial Regex TelegramTopicId();
}
