namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>
/// Makes a Poracle <c>humans.id</c> safe to write to a log.
/// </summary>
/// <remarks>
/// A webhook's id is its URL, and a Discord webhook URL carries the token that lets anyone post to the
/// channel: <c>https://discord.com/api/webhooks/&lt;id&gt;/&lt;token&gt;</c>. Every log line that named a
/// user, a target, a grant or a delegate could therefore print a credential. Route every such argument
/// through <see cref="Id"/>. See #1038.
/// </remarks>
public static class LogSafe
{
    private const string Mask = "***";

    /// <summary>
    /// Returns <paramref name="id"/> with any secret part of a URL masked, and every other id unchanged.
    /// </summary>
    /// <remarks>
    /// A Discord webhook URL keeps its path up to the webhook's numeric id, which is not a secret and is
    /// what tells two webhooks apart in a log. Any other http(s) URL keeps its scheme and host only: there is
    /// no telling where another service puts its secret, and some put it first (Telegram's
    /// <c>/bot&lt;token&gt;/sendMessage</c>). User info, query and fragment are always dropped. Surrounding
    /// whitespace is ignored, and a scheme-less path containing <c>/webhooks/</c> is read as https. Discord
    /// and Telegram ids, topic ids (<c>chat:topic</c>) and names pass through unchanged.
    /// </remarks>
    public static string Id(string? id)
    {
        if (id is null)
        {
            return "(none)";
        }

        // Poracle stores webhook ids as http(s) URLs, but a log argument can arrive padded, or as a
        // scheme-less webhook path copied out of a config file; both still carry the token.
        var candidate = id.Trim();
        var hasScheme = candidate.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        if (!hasScheme)
        {
            var schemelessWebhook = candidate.Contains("/webhooks/", StringComparison.OrdinalIgnoreCase)
                && !candidate.Any(char.IsWhiteSpace);
            if (!schemelessWebhook)
            {
                return id;
            }

            candidate = "https://" + candidate;
        }

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
        {
            return Mask;
        }

        var server = uri.GetComponents(UriComponents.SchemeAndServer, UriFormat.UriEscaped);
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (IsDiscordHost(uri.Host))
        {
            var webhooks = Array.FindIndex(segments, s => s.Equals("webhooks", StringComparison.OrdinalIgnoreCase));

            // Only a snowflake is kept. A segment that isn't one is not an id, and may be the token.
            if (webhooks >= 0 && webhooks + 1 < segments.Length && segments[webhooks + 1].All(char.IsAsciiDigit))
            {
                return $"{server}/{string.Join('/', segments[..(webhooks + 2)])}/{Mask}";
            }
        }

        return $"{server}/{Mask}";
    }

    private static bool IsDiscordHost(string host) =>
        host.Equals("discord.com", StringComparison.OrdinalIgnoreCase)
        || host.Equals("discordapp.com", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(".discord.com", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(".discordapp.com", StringComparison.OrdinalIgnoreCase);
}
