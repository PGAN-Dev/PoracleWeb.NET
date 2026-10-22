using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Pgan.PoracleWebNet.Api.Configuration;

namespace Pgan.PoracleWebNet.Tests.Configuration;

/// <summary>
/// Tests for the response security headers (issue #383).
/// The Referrer-Policy assertions are the point of this file: the value has to stay
/// cross-origin-suppressing (so remote image hosts don't learn the instance origin) while
/// still sending a same-origin referrer, which AuthController's login/logout redirects
/// depend on.
/// </summary>
public class SecurityHeadersTests
{
    /// <summary>
    /// The CSP written out in one piece. Guards the concatenated string in
    /// <see cref="SecurityHeaders"/> against a typo. connect-src carries the basemap tile hosts
    /// because the browser revalidates a stale cached tile in the background
    /// (stale-while-revalidate) and checks that request against connect-src, not img-src.
    /// </summary>
    private const string OriginalCsp =
        "default-src 'self'; script-src 'self' 'unsafe-hashes' 'sha256-MhtPZXr7+LpJUY5qtMutB+qWfQtMaPccfe7QXtCcEYc=' https://telegram.org; style-src 'self' 'unsafe-inline'; font-src 'self' https://fonts.gstatic.com; img-src 'self' data: https:; connect-src 'self' https://raw.githubusercontent.com https://tile.openstreetmap.org https://server.arcgisonline.com https://*.basemaps.cartocdn.com https://tiles.stadiamaps.com; frame-src https://oauth.telegram.org";

    [Fact]
    public void Apply_SetsReferrerPolicy_ToSameOrigin()
    {
        IHeaderDictionary headers = new HeaderDictionary();

        SecurityHeaders.Apply(headers);

        Assert.Equal("same-origin", headers["Referrer-Policy"]);
    }

    /// <summary>
    /// A referrer policy of no-referrer blanks the Referer header on same-origin requests
    /// too, which breaks the origin recovery in AuthController's DiscordLogin, OIDC login,
    /// and OIDC logout handlers -- they would silently fall back to this host's own origin
    /// and redirect users to the wrong place after a provider callback. If this assertion
    /// fails, read the remarks on SecurityHeaders.ReferrerPolicy before changing it.
    /// </summary>
    [Fact]
    public void ReferrerPolicy_IsNotNoReferrer_SoAuthRedirectsKeepWorking()
    {
        Assert.NotEqual("no-referrer", SecurityHeaders.ReferrerPolicy);
    }

    /// <summary>
    /// The whole reason for #383: the policy must not send anything cross-origin. The two
    /// values that leak the origin are the browser default and the explicit unsafe opt-outs.
    /// </summary>
    [Theory]
    [InlineData("strict-origin-when-cross-origin")]
    [InlineData("no-referrer-when-downgrade")]
    [InlineData("origin")]
    [InlineData("origin-when-cross-origin")]
    [InlineData("unsafe-url")]
    public void ReferrerPolicy_DoesNotLeakOriginCrossOrigin(string leakyPolicy)
    {
        Assert.NotEqual(leakyPolicy, SecurityHeaders.ReferrerPolicy);
    }

    [Fact]
    public void Apply_SetsContentSecurityPolicy_UnchangedFromTheInlineVersion()
    {
        IHeaderDictionary headers = new HeaderDictionary();

        SecurityHeaders.Apply(headers);

        Assert.Equal(OriginalCsp, headers.ContentSecurityPolicy);
    }

    [Fact]
    public void Apply_SetsTheRemainingHardeningHeaders()
    {
        IHeaderDictionary headers = new HeaderDictionary();

        SecurityHeaders.Apply(headers);

        Assert.Equal("nosniff", headers.XContentTypeOptions);
        Assert.Equal("DENY", headers.XFrameOptions);
        Assert.Equal("0", headers.XXSSProtection);
    }

    [Fact]
    public void Apply_OverwritesAnyValuePresetByAnUpstreamProxyOrHost()
    {
        IHeaderDictionary headers = new HeaderDictionary
        {
            ["Referrer-Policy"] = "unsafe-url"
        };

        SecurityHeaders.Apply(headers);

        Assert.Equal("same-origin", headers["Referrer-Policy"]);
    }

    /// <summary>
    /// Every tile host in the SPA's basemap catalogue must be in connect-src. OpenStreetMap
    /// answers with stale-while-revalidate, and Chromium checks the background revalidation of a
    /// cached tile against connect-src: without the host the console fills with "Refused to
    /// connect" on every map page. img-src stays as it was.
    /// </summary>
    [Fact]
    public void ConnectSrc_AllowsEveryBasemapCatalogueTileHost()
    {
        var catalogue = File.ReadAllText(Path.Combine(
            RepoRoot(), "Applications", "Pgan.PoracleWebNet.App", "ClientApp", "src", "app", "shared", "utils", "basemaps.ts"));
        var hosts = Regex.Matches(catalogue, @"(?:url|darkUrl):\s*'https://([^/']+)/")
            .Select(m => m.Groups[1].Value.Replace("{s}.", "*.", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        Assert.NotEmpty(hosts);

        var connectSrc = SecurityHeaders.ContentSecurityPolicy
            .Split(';', StringSplitOptions.TrimEntries)
            .Single(d => d.StartsWith("connect-src ", StringComparison.Ordinal))
            .Split(' ');

        foreach (var host in hosts)
        {
            Assert.Contains("https://" + host, connectSrc);
        }
    }

    /// <summary>
    /// The tile hosts went into connect-src, not into a wider img-src: images were already open to
    /// any https host and must stay exactly that.
    /// </summary>
    [Fact]
    public void ImgSrc_IsUnchanged()
    {
        var imgSrc = SecurityHeaders.ContentSecurityPolicy
            .Split(';', StringSplitOptions.TrimEntries)
            .Single(d => d.StartsWith("img-src ", StringComparison.Ordinal));

        Assert.Equal("img-src 'self' data: https:", imgSrc);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Could not locate the repository root.");
    }

    [Fact]
    public void Apply_Throws_WhenHeadersAreNull() =>
        Assert.Throws<ArgumentNullException>(() => SecurityHeaders.Apply(null!));
}
