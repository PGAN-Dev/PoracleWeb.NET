using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using Pgan.PoracleWebNet.Api.Configuration;

namespace Pgan.PoracleWebNet.Tests.Configuration;

/// <summary>
/// A token re-issue must not extend the session or carry a stale <c>isAdmin</c> claim. See #624.
/// </summary>
public class JwtServiceReissueTests
{
    private static readonly JwtSettings Settings = new()
    {
        Secret = "this-is-a-test-signing-key-long-enough-for-hmac-sha256",
        Issuer = "PoracleWeb.Api",
        Audience = "PoracleWeb.App",
        ExpirationMinutes = 1440,
    };

    private static ClaimsPrincipal PrincipalExpiringIn(TimeSpan remaining, bool isAdmin = true)
    {
        var expiresAt = DateTimeOffset.UtcNow.Add(remaining).ToUnixTimeSeconds();
        var identity = new ClaimsIdentity(
        [
            new Claim("userId", "u1"),
            new Claim("username", "Tester"),
            new Claim("isAdmin", isAdmin.ToString().ToLowerInvariant()),
            new Claim("profileNo", "0"),
            new Claim("exp", expiresAt.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        ], "TestAuth");

        return new ClaimsPrincipal(identity);
    }

    private static JwtSecurityToken Read(string token) => new JwtSecurityTokenHandler().ReadJwtToken(token);

    [Fact]
    public void ReissueKeepsTheOriginalExpiryRatherThanStartingAFreshLifetime()
    {
        // An OIDC access token is deliberately short so revocation propagates. Re-issuing at the
        // configured default turned a 30-minute session into a 24-hour one on the first profile switch.
        var sut = new JwtService(Options.Create(Settings));
        var principal = PrincipalExpiringIn(TimeSpan.FromMinutes(30));

        var token = Read(sut.GenerateTokenWithReplacedProfile(principal, 2));

        var remaining = token.ValidTo - DateTime.UtcNow;
        Assert.InRange(remaining.TotalMinutes, 25, 35);
    }

    [Fact]
    public void ReissueDoesNotRenewASessionThatIsAlmostOver()
    {
        var sut = new JwtService(Options.Create(Settings));
        var principal = PrincipalExpiringIn(TimeSpan.FromMinutes(2));

        var token = Read(sut.GenerateTokenWithReplacedProfile(principal, 2));

        Assert.True((token.ValidTo - DateTime.UtcNow).TotalMinutes < 10);
    }

    [Fact]
    public void ReissueReplacesIsAdminWhenAFreshValueIsSupplied()
    {
        // Copied verbatim, the claim outlived the rights it described: a de-admined user who switched
        // profile once a day never lost access.
        var sut = new JwtService(Options.Create(Settings));
        var principal = PrincipalExpiringIn(TimeSpan.FromHours(1), isAdmin: true);

        var token = Read(sut.GenerateTokenWithReplacedProfile(principal, 2, isAdmin: false));

        Assert.Equal("false", token.Claims.Single(c => c.Type == "isAdmin").Value);
    }

    [Fact]
    public void ReissueKeepsTheExistingIsAdminWhenNoFreshValueIsSupplied()
    {
        var sut = new JwtService(Options.Create(Settings));
        var principal = PrincipalExpiringIn(TimeSpan.FromHours(1), isAdmin: true);

        var token = Read(sut.GenerateTokenWithReplacedProfile(principal, 2));

        Assert.Equal("true", token.Claims.Single(c => c.Type == "isAdmin").Value);
    }

    [Fact]
    public void ReissueFallsBackToTheConfiguredLifetimeWhenThePrincipalCarriesNoExpiry()
    {
        var sut = new JwtService(Options.Create(Settings));
        var identity = new ClaimsIdentity([new Claim("userId", "u1"), new Claim("profileNo", "0")], "TestAuth");

        var token = Read(sut.GenerateTokenWithReplacedProfile(new ClaimsPrincipal(identity), 1));

        Assert.InRange((token.ValidTo - DateTime.UtcNow).TotalMinutes, 1400, 1441);
    }

    /// <summary>
    /// The remaining lifetime used to be rounded UP to whole minutes and then counted from now, so every
    /// re-issue moved the expiry later by up to a minute. A caller re-issuing once a minute -- a profile
    /// switch, which an impersonation session can do -- held the token at the same distance from expiry
    /// forever. The re-issued token must expire exactly when the original did.
    /// </summary>
    [Theory]
    [InlineData(10 * 60 + 5)]
    [InlineData(59)]
    [InlineData(1)]
    public void ReissueNeverMovesTheExpiryLater(int secondsLeft)
    {
        var sut = new JwtService(Options.Create(Settings));
        var principal = PrincipalExpiringIn(TimeSpan.FromSeconds(secondsLeft));
        var originalExpiry = long.Parse(principal.FindFirst("exp")!.Value, System.Globalization.CultureInfo.InvariantCulture);

        var token = Read(sut.GenerateTokenWithReplacedProfile(principal, 2));

        Assert.Equal(originalExpiry, new DateTimeOffset(token.ValidTo, TimeSpan.Zero).ToUnixTimeSeconds());
    }

    /// <summary>
    /// JwtBearer accepts a token up to five minutes past its expiry (the default clock skew), and the old
    /// one-minute floor then minted a token valid for another minute from now. Re-issuing inside the skew
    /// window every minute kept a dead session alive indefinitely.
    /// </summary>
    [Fact]
    public void ReissueOfATokenInsideTheClockSkewWindowDoesNotRevive()
    {
        var sut = new JwtService(Options.Create(Settings));
        var principal = PrincipalExpiringIn(TimeSpan.FromMinutes(-3));
        var originalExpiry = long.Parse(principal.FindFirst("exp")!.Value, System.Globalization.CultureInfo.InvariantCulture);

        var token = Read(sut.GenerateTokenWithReplacedProfile(principal, 2));

        Assert.Equal(originalExpiry, new DateTimeOffset(token.ValidTo, TimeSpan.Zero).ToUnixTimeSeconds());
    }

    [Fact]
    public void ReissueOfAnImpersonationTokenKeepsTheImpersonatorAndTheExpiry()
    {
        var sut = new JwtService(Options.Create(Settings));
        var principal = PrincipalExpiringIn(TimeSpan.FromMinutes(90), isAdmin: false);
        ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim("impersonatedBy", "delegate-1"));
        var originalExpiry = long.Parse(principal.FindFirst("exp")!.Value, System.Globalization.CultureInfo.InvariantCulture);

        var token = Read(sut.GenerateTokenWithReplacedProfile(principal, 3));

        Assert.Equal("delegate-1", token.Claims.Single(c => c.Type == "impersonatedBy").Value);
        Assert.Equal(originalExpiry, new DateTimeOffset(token.ValidTo, TimeSpan.Zero).ToUnixTimeSeconds());
    }

    [Fact]
    public void ReissueStillReplacesTheProfileNumber()
    {
        var sut = new JwtService(Options.Create(Settings));

        var token = Read(sut.GenerateTokenWithReplacedProfile(PrincipalExpiringIn(TimeSpan.FromHours(1)), 7));

        Assert.Equal("7", token.Claims.Single(c => c.Type == "profileNo").Value);
    }
}
