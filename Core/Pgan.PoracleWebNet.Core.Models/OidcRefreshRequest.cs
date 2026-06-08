namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>Body for <c>POST /api/auth/oidc/refresh</c> and <c>/oidc/refresh/revoke</c>: the opaque
/// PoracleWeb refresh token the browser holds in localStorage.</summary>
public sealed class OidcRefreshRequest
{
    public string? RefreshToken
    {
        get; set;
    }
}
