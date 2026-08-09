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

    [Fact]
    public void ReissueStillReplacesTheProfileNumber()
    {
        var sut = new JwtService(Options.Create(Settings));

        var token = Read(sut.GenerateTokenWithReplacedProfile(PrincipalExpiringIn(TimeSpan.FromHours(1)), 7));

        Assert.Equal("7", token.Claims.Single(c => c.Type == "profileNo").Value);
    }
}
