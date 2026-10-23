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
    /// what tells two webhooks apart in a log. Any other http(s) URL keeps its host, and its first path
    /// segment when more follow, since there is no telling where another service puts its secret. User
    /// info, query and fragment are always dropped. Discord and Telegram ids, topic ids (<c>chat:topic</c>) and names pass through.
    /// </remarks>
    public static string Id(string? id)
    {
        if (id is null)
        {
            return "(none)";
        }

        var looksLikeUrl = id.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || id.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        if (!looksLikeUrl)
        {
            return id;
        }

        if (!Uri.TryCreate(id, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
        {
            return Mask;
        }

        var server = uri.GetComponents(UriComponents.SchemeAndServer, UriFormat.UriEscaped);
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (IsDiscordHost(uri.Host))
        {
            var webhooks = Array.FindIndex(segments, s => s.Equals("webhooks", StringComparison.OrdinalIgnoreCase));
            if (webhooks >= 0 && webhooks + 1 < segments.Length)
            {
                return $"{server}/{string.Join('/', segments[..(webhooks + 2)])}/{Mask}";
            }
        }

        // A lone segment may be the secret itself, so it is kept only when something follows it.
        return segments.Length > 1 ? $"{server}/{segments[0]}/{Mask}" : $"{server}/{Mask}";
    }

    private static bool IsDiscordHost(string host) =>
        host.Equals("discord.com", StringComparison.OrdinalIgnoreCase)
        || host.Equals("discordapp.com", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(".discord.com", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(".discordapp.com", StringComparison.OrdinalIgnoreCase);
}
