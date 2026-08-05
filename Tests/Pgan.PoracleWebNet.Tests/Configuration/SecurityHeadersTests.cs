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
    /// The CSP exactly as it was written inline in Program.cs before being extracted into
    /// <see cref="SecurityHeaders"/>. Guards the extraction against a typo in the
    /// concatenated string.
    /// </summary>
    private const string OriginalCsp =
        "default-src 'self'; script-src 'self' 'unsafe-hashes' 'sha256-MhtPZXr7+LpJUY5qtMutB+qWfQtMaPccfe7QXtCcEYc=' https://telegram.org; style-src 'self' 'unsafe-inline'; font-src 'self' https://fonts.gstatic.com; img-src 'self' data: https:; connect-src 'self' https://raw.githubusercontent.com; frame-src https://oauth.telegram.org";

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

    [Fact]
    public void Apply_Throws_WhenHeadersAreNull() =>
        Assert.Throws<ArgumentNullException>(() => SecurityHeaders.Apply(null!));
}
