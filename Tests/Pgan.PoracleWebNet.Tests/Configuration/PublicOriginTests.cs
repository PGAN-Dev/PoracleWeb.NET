using Pgan.PoracleWebNet.Api.Configuration;

namespace Pgan.PoracleWebNet.Tests.Configuration;

/// <summary>
/// PUBLIC_URL is validated at startup so a typo surfaces there rather than as an OAuth provider
/// rejecting a malformed redirect_uri, which names neither the setting nor the mistake.
/// </summary>
public class PublicOriginTests
{
    [Theory]
    [InlineData("https://poracle.example.com", "https://poracle.example.com")]
    [InlineData("http://192.168.1.50:8082", "http://192.168.1.50:8082")]
    [InlineData("https://poracle.example.com/", "https://poracle.example.com")]
    [InlineData("  https://poracle.example.com  ", "https://poracle.example.com")]
    [InlineData("https://poracle.example.com:8443", "https://poracle.example.com:8443")]
    public void AcceptsAnOriginAndStripsTheTrailingSlash(string configured, string expected)
    {
        Assert.True(PublicOrigin.TryNormalize(configured, out var normalized, out var error));
        Assert.Equal(expected, normalized);
        Assert.Null(error);
    }

    /// <summary>Unset is the documented default, not an error -- callers fall back to the request.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TreatsUnsetAsAbsentRatherThanInvalid(string? configured)
    {
        Assert.False(PublicOrigin.TryNormalize(configured, out var normalized, out var error));
        Assert.Equal(string.Empty, normalized);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("poracle.example.com")]                      // no scheme
    [InlineData("ftp://poracle.example.com")]                // wrong scheme
    [InlineData("https://poracle.example.com/poracle")]      // path
    [InlineData("https://poracle.example.com/?a=b")]         // query
    [InlineData("https://poracle.example.com/#frag")]        // fragment
    [InlineData("not a url at all")]
    public void RejectsValuesThatWouldProduceABrokenCallbackUri(string configured)
    {
        Assert.False(PublicOrigin.TryNormalize(configured, out _, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void NormalizeOrNullReturnsNullForBothUnsetAndInvalid()
    {
        Assert.Null(PublicOrigin.NormalizeOrNull(null));
        Assert.Null(PublicOrigin.NormalizeOrNull("https://example.com/with/path"));
        Assert.Equal("https://example.com", PublicOrigin.NormalizeOrNull("https://example.com"));
    }
}
