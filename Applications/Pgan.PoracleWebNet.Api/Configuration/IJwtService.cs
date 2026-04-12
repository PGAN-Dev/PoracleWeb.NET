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
    /// Generates a JWT for an impersonated user. Includes an <c>impersonatedBy</c> claim
    /// identifying the admin who initiated the impersonation.
    /// </summary>
    string GenerateImpersonationToken(UserInfo user, string impersonatedBy);

    /// <summary>
    /// Generates a JWT by copying claims from an existing <see cref="ClaimsPrincipal"/>
    /// and replacing <c>profileNo</c>. Framework-injected claims (<c>exp</c>, <c>nbf</c>,
    /// <c>iat</c>, <c>iss</c>, <c>aud</c>) are filtered out to avoid duplication.
    /// </summary>
    string GenerateTokenWithReplacedProfile(ClaimsPrincipal existingPrincipal, int profileNo);
}
