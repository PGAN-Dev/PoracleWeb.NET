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
        //
        // And it must keep the expiry to the second. The remaining lifetime used to be rounded UP to whole
        // minutes and counted again from now, with a one-minute floor, so every re-issue moved the expiry
        // later: by up to a minute each time, or by a full minute for a token already inside JwtBearer's
        // five-minute clock skew. Re-issuing once a minute therefore renewed a session forever -- and an
        // impersonation session, whose authority is exactly what revocation has to end, could do that
        // with nothing more than a profile switch.
        var expiresAt = OriginalExpiry(existingPrincipal);
        if (isAdmin is { } resolved)
        {
            // Copied verbatim, isAdmin outlived the rights it described: nothing revalidates the claim,
            // so de-admining someone had no effect while they kept switching profile.
            claims.RemoveAll(c => string.Equals(c.Type, "isAdmin", StringComparison.Ordinal));
            claims.Add(new Claim("isAdmin", resolved.ToString().ToLowerInvariant()));
        }

        return expiresAt is { } expiry
            ? this.WriteToken(claims, expiry)
            : this.WriteToken(claims);
    }

    /// <summary>
    /// The principal's own <c>exp</c>, or null when it carries none.
    /// </summary>
    /// <remarks>
    /// Returned as the instant itself rather than as a lifetime, so the re-issued token expires when the
    /// original did and never a second later. A token already past its <c>exp</c> but still accepted
    /// under the clock skew is re-issued already expired, which is what it is.
    /// </remarks>
    private static DateTime? OriginalExpiry(ClaimsPrincipal principal)
    {
        var exp = principal.FindFirst("exp")?.Value ?? principal.FindFirst(JwtRegisteredClaimNames.Exp)?.Value;
        return long.TryParse(exp, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime
            : null;
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

    private string WriteToken(List<Claim> claims, int lifetimeMinutes) =>
        this.WriteToken(claims, DateTime.UtcNow.AddMinutes(lifetimeMinutes));

    private string WriteToken(List<Claim> claims, DateTime expiresUtc)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(this._settings.Secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: this._settings.Issuer,
            audience: this._settings.Audience,
            claims: claims,
            expires: expiresUtc,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
