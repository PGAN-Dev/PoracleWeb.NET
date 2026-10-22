namespace Pgan.PoracleWebNet.Api.Configuration;

/// <summary>
/// Response security headers applied to every request by the middleware in Program.cs.
/// Extracted from an inline lambda so the policy values are assertable in unit tests
/// without booting the whole app (issue #383).
/// </summary>
public static class SecurityHeaders
{
    /// <summary>
    /// Sends the full referrer on same-origin requests and nothing at all cross-origin.
    /// </summary>
    /// <remarks>
    /// Cross-origin suppression is the point: without it, every remote image host the SPA
    /// touches (uicons on raw.githubusercontent.com, Discord avatars on cdn.discordapp.com,
    /// Google Fonts) learns the origin of the PoracleWeb instance the user is browsing,
    /// which for a private instance is the thing worth not disclosing. Issue #383.
    ///
    /// Do NOT tighten this to <c>no-referrer</c>. <c>AuthController</c> reads the Referer
    /// header on the login and logout entry points (DiscordLogin, the OIDC login path, and
    /// OIDC RP-initiated logout) to recover which frontend origin the user came from,
    /// validate it against the configured CORS origins, and redirect back there after the
    /// provider callback. Those reads are same-origin when the SPA is served by this host,
    /// so <c>same-origin</c> keeps them working; <c>no-referrer</c> would blank them and
    /// silently bounce users to this host's own origin after login instead.
    /// </remarks>
    public const string ReferrerPolicy = "same-origin";

    /// <remarks>
    /// connect-src names the basemap catalogue's tile hosts (<c>shared/utils/basemaps.ts</c>). The
    /// SPA loads tiles as images, but OpenStreetMap answers with <c>stale-while-revalidate</c>, and
    /// Chromium checks the background revalidation of a stale cached tile against connect-src rather
    /// than img-src. Without the hosts every map page logs "Refused to connect". A custom tile URL an
    /// admin configures isn't listed; its revalidations are refused the same way and the cached tile
    /// is still drawn. <c>SecurityHeadersTests</c> fails if a catalogue host is missing here.
    /// </remarks>
    public const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self' 'unsafe-hashes' 'sha256-MhtPZXr7+LpJUY5qtMutB+qWfQtMaPccfe7QXtCcEYc=' https://telegram.org; "
        + "style-src 'self' 'unsafe-inline'; font-src 'self' https://fonts.gstatic.com; img-src 'self' data: https:; "
        + "connect-src 'self' https://raw.githubusercontent.com https://tile.openstreetmap.org https://server.arcgisonline.com "
        + "https://*.basemaps.cartocdn.com https://tiles.stadiamaps.com; frame-src https://oauth.telegram.org";

    /// <summary>
    /// Stamps the security headers onto an outgoing response.
    /// </summary>
    public static void Apply(IHeaderDictionary headers)
    {
        ArgumentNullException.ThrowIfNull(headers);

        headers.XContentTypeOptions = "nosniff";
        headers.XFrameOptions = "DENY";
        headers.XXSSProtection = "0";
        headers["Referrer-Policy"] = ReferrerPolicy;
        headers.ContentSecurityPolicy = ContentSecurityPolicy;
    }
}
