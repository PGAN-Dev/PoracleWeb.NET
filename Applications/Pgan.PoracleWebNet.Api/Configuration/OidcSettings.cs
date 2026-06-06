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
}
