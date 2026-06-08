using System.Text.Json;

namespace Pgan.PoracleWebNet.Api.Services.Oidc;

/// <summary>
/// The token/userinfo result from the external OIDC provider. <see cref="RefreshToken"/> is
/// nullable: a provider may issue one only with <c>offline_access</c>, and on a refresh grant a
/// non-rotating provider returns none (the caller then keeps reusing the prior token).
/// <see cref="ExpiresIn"/> is nullable for providers that omit it.
/// </summary>
public sealed record OidcTokenResult(string AccessToken, string? RefreshToken, int? ExpiresIn);

/// <summary>
/// Provider-agnostic HTTP client for the external OIDC endpoints. Encapsulates the
/// authorization-code exchange, the refresh-token grant, and the userinfo fetch — including the
/// configurable token-endpoint client-authentication method (<c>client_secret_post</c> vs
/// <c>client_secret_basic</c>). Relies only on spec-standard OAuth2/OIDC; no discovery, JWKS, or
/// id_token required.
/// </summary>
public interface IOidcClient
{
    /// <summary>Exchanges an authorization code for tokens. Returns null on any provider error.</summary>
    Task<OidcTokenResult?> ExchangeCodeAsync(string code, string redirectUri, string? codeVerifier);

    /// <summary>Redeems a refresh token (grant_type=refresh_token). Returns null on any provider error.</summary>
    Task<OidcTokenResult?> RefreshAsync(string refreshToken);

    /// <summary>Fetches the userinfo claims with the given access token. Returns null on failure.</summary>
    Task<JsonElement?> GetUserInfoAsync(string accessToken);
}
