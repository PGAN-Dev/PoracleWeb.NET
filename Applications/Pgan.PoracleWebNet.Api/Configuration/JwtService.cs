using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Api.Configuration;

public sealed class JwtService(IOptions<JwtSettings> jwtSettings) : IJwtService
{
    private readonly JwtSettings _settings = jwtSettings.Value;

    /// <summary>
    /// Registered JWT claim types that must NOT be copied from an existing token —
    /// they are set automatically by the <see cref="JwtSecurityToken"/> constructor.
    /// Copying them produces duplicate claims and carries over stale expiry/issuer values.
    /// </summary>
    private static readonly HashSet<string> RegisteredClaimTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "exp", "nbf", "iat", "iss", "aud",
        JwtRegisteredClaimNames.Exp,
        JwtRegisteredClaimNames.Nbf,
        JwtRegisteredClaimNames.Iat,
        JwtRegisteredClaimNames.Iss,
        JwtRegisteredClaimNames.Aud,
    };

    public string GenerateToken(UserInfo user)
    {
        var claims = BuildClaims(user);
        return this.WriteToken(claims);
    }

    public string GenerateToken(UserInfo user, int lifetimeMinutes)
    {
        var claims = BuildClaims(user);
        return this.WriteToken(claims, lifetimeMinutes);
    }

    public string GenerateImpersonationToken(UserInfo user, string impersonatedBy)
    {
        var claims = BuildClaims(user);
        claims.Add(new Claim("impersonatedBy", impersonatedBy));
        return this.WriteToken(claims);
    }

    public string GenerateTokenWithReplacedProfile(ClaimsPrincipal existingPrincipal, int profileNo, bool? isAdmin = null)
    {
        var claims = new List<Claim>();
        foreach (var claim in existingPrincipal.Claims)
        {
            if (string.Equals(claim.Type, "profileNo", StringComparison.Ordinal))
            {
                continue;
            }

            // Skip framework-injected registered claims to avoid duplicates
            if (RegisteredClaimTypes.Contains(claim.Type))
            {
                continue;
            }

            claims.Add(new Claim(claim.Type, claim.Value));
        }

        claims.Add(new Claim("profileNo", profileNo.ToString(CultureInfo.InvariantCulture)));

        // A re-issue must not extend the session. This used to end in WriteToken(claims), which applies
        // the configured default of 24 hours -- so an OIDC login's deliberately short 30-minute access
        // token became a day-long one on the first profile switch, and a user who switched profile once
        // a day never expired at all. Revocation is supposed to propagate within roughly one access
        // token's lifetime; renewing on re-issue quietly removed that bound. See #624.
        var remaining = RemainingMinutes(existingPrincipal);
        if (isAdmin is { } resolved)
        {
            // Copied verbatim, isAdmin outlived the rights it described: nothing revalidates the claim,
            // so de-admining someone had no effect while they kept switching profile.
            claims.RemoveAll(c => string.Equals(c.Type, "isAdmin", StringComparison.Ordinal));
            claims.Add(new Claim("isAdmin", resolved.ToString().ToLowerInvariant()));
        }

        return remaining is { } minutes
            ? this.WriteToken(claims, minutes)
            : this.WriteToken(claims);
    }

    /// <summary>
    /// Whole minutes left on the principal's own <c>exp</c>, or null when it carries none.
    /// </summary>
    private static int? RemainingMinutes(ClaimsPrincipal principal)
    {
        var exp = principal.FindFirst("exp")?.Value ?? principal.FindFirst(JwtRegisteredClaimNames.Exp)?.Value;
        if (!long.TryParse(exp, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
        {
            return null;
        }

        var remaining = DateTimeOffset.FromUnixTimeSeconds(seconds) - DateTimeOffset.UtcNow;

        // The request authenticated, so the token was live when it arrived; a floor of one minute keeps
        // a token that expires mid-request from being re-issued already dead.
        return Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));
    }

    private static List<Claim> BuildClaims(UserInfo user)
    {
        var claims = new List<Claim>
        {
            new("userId", user.Id),
            new("username", user.Username),
            new("type", user.Type),
            new("isAdmin", user.IsAdmin.ToString().ToLowerInvariant()),
            new("enabled", user.Enabled.ToString().ToLowerInvariant()),
            new("profileNo", user.ProfileNo.ToString(CultureInfo.InvariantCulture)),
        };

        if (!string.IsNullOrEmpty(user.AvatarUrl))
        {
            claims.Add(new Claim("avatarUrl", user.AvatarUrl));
        }

        if (user.ManagedWebhooks is { Length: > 0 })
        {
            claims.Add(new Claim("managedWebhooks", string.Join(',', user.ManagedWebhooks)));
        }

        return claims;
    }

    private string WriteToken(List<Claim> claims) => this.WriteToken(claims, this._settings.ExpirationMinutes);

    private string WriteToken(List<Claim> claims, int lifetimeMinutes)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(this._settings.Secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: this._settings.Issuer,
            audience: this._settings.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(lifetimeMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
