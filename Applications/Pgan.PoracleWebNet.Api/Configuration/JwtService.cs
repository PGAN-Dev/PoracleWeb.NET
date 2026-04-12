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

    public string GenerateImpersonationToken(UserInfo user, string impersonatedBy)
    {
        var claims = BuildClaims(user);
        claims.Add(new Claim("impersonatedBy", impersonatedBy));
        return this.WriteToken(claims);
    }

    public string GenerateTokenWithReplacedProfile(ClaimsPrincipal existingPrincipal, int profileNo)
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
        return this.WriteToken(claims);
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

    private string WriteToken(List<Claim> claims)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(this._settings.Secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: this._settings.Issuer,
            audience: this._settings.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(this._settings.ExpirationMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
