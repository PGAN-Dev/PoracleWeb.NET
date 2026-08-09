using System.Security.Claims;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Api.Configuration;

/// <summary>
/// Centralized JWT token generation. Replaces duplicated token-creation logic
/// across AuthController, ProfileController, ProfileOverviewController, and AdminController.
/// </summary>
public interface IJwtService
{
    /// <summary>
    /// Generates a fresh JWT from a <see cref="UserInfo"/> object. All claims are built
    /// from the model — no stale claims are carried over from an existing token.
    /// </summary>
    string GenerateToken(UserInfo user);

    /// <summary>
    /// Generates a fresh JWT with an explicit lifetime (minutes), overriding the configured
    /// default. Used for refresh-backed OIDC sessions, which are deliberately short-lived so
    /// provider-side revocation propagates quickly via silent refresh.
    /// </summary>
    string GenerateToken(UserInfo user, int lifetimeMinutes);

    /// <summary>
    /// Generates a JWT for an impersonated user. Includes an <c>impersonatedBy</c> claim
    /// identifying the admin who initiated the impersonation.
    /// </summary>
    string GenerateImpersonationToken(UserInfo user, string impersonatedBy);

    /// <summary>
    /// Generates a JWT by copying claims from an existing <see cref="ClaimsPrincipal"/>
    /// and replacing <c>profileNo</c>. Framework-injected claims (<c>exp</c>, <c>nbf</c>,
    /// <c>iat</c>, <c>iss</c>, <c>aud</c>) are filtered out to avoid duplication.
    /// <para>
    /// The re-issued token keeps the original <c>exp</c> rather than starting a fresh lifetime -- a
    /// re-issue must never extend a session. Pass <paramref name="isAdmin"/> to replace the copied
    /// claim with a freshly resolved value. See #624.
    /// </para>
    /// </summary>
    string GenerateTokenWithReplacedProfile(ClaimsPrincipal existingPrincipal, int profileNo, bool? isAdmin = null);
}
