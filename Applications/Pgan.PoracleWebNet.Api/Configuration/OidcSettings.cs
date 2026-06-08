namespace Pgan.PoracleWebNet.Api.Configuration;

/// <summary>
/// Configuration for a generic external OIDC / OAuth2 login provider. This lets any
/// self-hoster delegate PoracleWeb login to their own identity provider (PGAN's
/// PogoAlerts being one instance). It mirrors the Discord flow, parameterized by config.
/// All values come from env/appsettings (the provider secret is never stored in the DB);
/// the admin runtime on/off toggle is the separate <c>enable_oidc</c> site setting.
/// </summary>
public class OidcSettings
{
    /// <summary>Master switch from server config. When false the provider is hidden regardless of other values.</summary>
    public bool Enabled { get; set; }

    /// <summary>Display name shown on the login button, e.g. "PogoAlerts".</summary>
    public string ProviderName { get; set; } = string.Empty;

    /// <summary>Browser-facing authorization endpoint. For PogoAlerts this is e.g. https://pogoalerts.net/login.</summary>
    public string AuthorizationUrl { get; set; } = string.Empty;

    /// <summary>Token endpoint that exchanges the authorization code for an access token.</summary>
    public string TokenUrl { get; set; } = string.Empty;

    /// <summary>
    /// Optional OIDC RP-initiated logout (end-session) endpoint. When set, signing out of
    /// PoracleWeb redirects the browser here with a <c>post_logout_redirect_uri</c> so the
    /// provider can also end its own session (true single logout). When empty, logout is
    /// local-only (the provider session survives). For PogoAlerts this is e.g.
    /// https://pogoalerts.net/logout.
    /// </summary>
    public string EndSessionUrl { get; set; } = string.Empty;

    /// <summary>UserInfo endpoint (OpenID Connect compatible) returning the user's claims.</summary>
    public string UserInfoUrl { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Space-delimited OAuth scopes requested at authorization time.</summary>
    public string Scopes { get; set; } = "openid profile email";

    /// <summary>
    /// UserInfo claim whose value is the Poracle <c>human</c> id (a Discord or Telegram id).
    /// Defaults to <c>discord_id</c> (PogoAlerts passes through the linked Discord id);
    /// falls back to <c>sub</c> when the configured claim is absent.
    /// </summary>
    public string IdentityClaim { get; set; } = "discord_id";

    /// <summary>UserInfo claim used as the display username.</summary>
    public string UsernameClaim { get; set; } = "preferred_username";

    /// <summary>UserInfo claim used as the avatar URL.</summary>
    public string AvatarClaim { get; set; } = "picture";

    /// <summary>
    /// Value written to the JWT <c>type</c> claim for users who log in via this provider.
    /// Defaults to <c>discord:user</c> so downstream admin/role resolution treats the
    /// passed-through Discord id consistently with a direct Discord login.
    /// </summary>
    public string IdentityType { get; set; } = "discord:user";

    /// <summary>Whether to use PKCE (Proof Key for Code Exchange) — recommended and supported by PogoAlerts.</summary>
    public bool UsePkce { get; set; } = true;

    /// <summary>
    /// Master opt-in for consuming the provider's refresh token (silent session renewal +
    /// revocation propagation). Default <c>false</c> — when off, behavior is identical to a
    /// plain login: the provider's tokens are discarded and the internal JWT lives its full
    /// <see cref="JwtSettings.ExpirationMinutes"/>. Requires the provider to actually issue a
    /// refresh token (standard providers gate that behind the <c>offline_access</c> scope —
    /// see <see cref="OfflineAccessScope"/>).
    /// </summary>
    public bool UseRefreshTokens { get; set; }

    /// <summary>
    /// Internal JWT lifetime (minutes) for refresh-backed OIDC sessions only. Kept short so a
    /// disable/revocation at the provider propagates within roughly one access-token lifetime.
    /// Other logins (Discord, Telegram, local, OIDC without refresh) are unaffected and keep
    /// <see cref="JwtSettings.ExpirationMinutes"/>.
    /// </summary>
    public int AccessTokenMinutes { get; set; } = 30;

    /// <summary>
    /// PoracleWeb-side absolute cap (days) on a refresh session/family before a real re-login is
    /// forced. Independent of the provider's own refresh-token lifetime; if the provider's token
    /// expires first, the refresh call fails and the session is revoked — correct either way.
    /// </summary>
    public int RefreshTokenLifetimeDays { get; set; } = 30;

    /// <summary>
    /// How long (days) a revoked/rotated <c>oidc_sessions</c> row is retained before the cleanup
    /// service deletes it. Revoked rows are kept briefly so a replayed old opaque token is still
    /// detected (and family-revoked) rather than silently 401ing; replay happens fast, so a short
    /// window suffices. Kept separate from <see cref="RefreshTokenLifetimeDays"/> so frequent
    /// rotation doesn't pile up 30 days of dead rows. Expired rows are deleted regardless of this.
    /// </summary>
    public int RevokedRetentionDays { get; set; } = 2;

    /// <summary>
    /// Scope appended to the authorization request (only when <see cref="UseRefreshTokens"/> is on
    /// and it isn't already present) so a standards-compliant provider issues a refresh token.
    /// Defaults to <c>offline_access</c>. Set empty for providers that issue refresh tokens
    /// unconditionally, or that use a non-standard mechanism (e.g. Google's
    /// <c>access_type=offline</c> appended directly to <see cref="AuthorizationUrl"/>).
    /// </summary>
    public string OfflineAccessScope { get; set; } = "offline_access";

    /// <summary>
    /// How client credentials are presented at the token endpoint: <c>client_secret_post</c>
    /// (default — credentials in the form body, what PogoAlerts uses) or <c>client_secret_basic</c>
    /// (HTTP Basic auth header — the default for Keycloak/Okta). Applies to both the
    /// authorization-code exchange and the refresh-token grant.
    /// </summary>
    public string TokenEndpointAuthMethod { get; set; } = "client_secret_post";
}
