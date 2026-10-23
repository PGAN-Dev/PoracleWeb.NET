using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Pgan.PoracleWebNet.Api.Configuration;

namespace Pgan.PoracleWebNet.Api.Services;

/// <summary>
/// Builds Discord's authorize URL and the cookies the callback checks it against. Sign-in and account
/// linking both go through here so the redirect URI, scope and cookie options cannot drift apart.
/// </summary>
internal static class DiscordAuthorizeRequest
{
    public static CookieOptions CookieOptions(HttpRequest request) => new()
    {
        HttpOnly = true,
        Secure = string.Equals(request.Scheme, "https", StringComparison.OrdinalIgnoreCase),
        SameSite = SameSiteMode.Lax,
        MaxAge = TimeSpan.FromMinutes(10),
    };

    /// <summary>Sets <c>oauth_state</c> and <c>oauth_origin</c> and returns the URL to send the browser to.</summary>
    public static string Build(HttpContext http, DiscordSettings settings, string selfOrigin, string[] allowedOrigins, out string callbackUri, out string state)
    {
        state = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var cookieOptions = CookieOptions(http.Request);
        http.Response.Cookies.Append("oauth_state", state, cookieOptions);

        // Where to send the browser back to. Validated against the configured origins so a forged
        // Referer cannot turn the callback into an open redirect.
        var origin = selfOrigin;
        var referer = http.Request.Headers.Referer.FirstOrDefault();
        if (!string.IsNullOrEmpty(referer) && Uri.TryCreate(referer, UriKind.Absolute, out var refererUri))
        {
            var refererOrigin = $"{refererUri.Scheme}://{refererUri.Authority}";
            if (allowedOrigins.Length > 0
                ? allowedOrigins.Any(o => string.Equals(o, refererOrigin, StringComparison.OrdinalIgnoreCase))
                : string.Equals(refererOrigin, selfOrigin, StringComparison.OrdinalIgnoreCase))
            {
                origin = refererOrigin;
            }
        }

        http.Response.Cookies.Append("oauth_origin", origin, cookieOptions);

        callbackUri = $"{selfOrigin}/api/auth/discord/callback";
        return "https://discordapp.com/api/oauth2/authorize" +
            $"?client_id={settings.ClientId}" +
            $"&redirect_uri={Uri.EscapeDataString(callbackUri)}" +
            "&response_type=code" +
            "&scope=identify" +
            "&prompt=none" +
            $"&state={Uri.EscapeDataString(state)}";
    }
}
