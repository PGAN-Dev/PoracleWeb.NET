using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Pgan.PoracleWebNet.Api.Configuration;
using Pgan.PoracleWebNet.Api.Services.Oidc;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Api.Controllers;

[Route("api/auth")]
[EnableRateLimiting("auth")]
public partial class AuthController(
    IHumanService humanService,
    IPoracleApiProxy poracleApiProxy,
    IPoracleHumanProxy humanProxy,
    ISiteSettingService siteSettingService,
    IWebhookDelegateService webhookDelegateService,
    IJwtService jwtService,
    IOidcClient oidcClient,
    IOidcSessionService oidcSessionService,
    IOptions<DiscordSettings> discordSettings,
    IOptions<TelegramSettings> telegramSettings,
    IOptions<OidcSettings> oidcSettings,
    IOptions<PoracleSettings> poracleSettings,
    IConfiguration configuration,
    ILogger<AuthController> logger) : BaseApiController
{
    private const string EnableDiscordKey = "enable_discord";
    private const string EnableTelegramKey = "enable_telegram";
    private const string EnableOidcKey = "enable_oidc";
    private const string EnableOidcSloKey = "enable_oidc_slo";

    private readonly IHumanService _humanService = humanService;
    private readonly IPoracleApiProxy _poracleApiProxy = poracleApiProxy;
    private readonly IPoracleHumanProxy _humanProxy = humanProxy;
    private readonly ISiteSettingService _siteSettingService = siteSettingService;
    private readonly IWebhookDelegateService _webhookDelegateService = webhookDelegateService;
    private readonly IJwtService _jwtService = jwtService;
    private readonly IOidcClient _oidcClient = oidcClient;
    private readonly IOidcSessionService _oidcSessionService = oidcSessionService;
    private readonly DiscordSettings _discordSettings = discordSettings.Value;
    private readonly TelegramSettings _telegramSettings = telegramSettings.Value;
    private readonly OidcSettings _oidcSettings = oidcSettings.Value;
    private readonly PoracleSettings _poracleSettings = poracleSettings.Value;
    private readonly string[] _allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
    private readonly ILogger<AuthController> _logger = logger;

    [AllowAnonymous]
    [HttpGet("discord/login")]
    public async Task<IActionResult> DiscordLogin()
    {
        // No early gate here — admins must be able to log in even when Discord is disabled.
        // The enable_discord check is enforced in DiscordCallback() after we know whether the user is an admin.

        // Generate a random state value for CSRF protection
        var state = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        var isHttps = string.Equals(this.Request.Scheme, "https", StringComparison.OrdinalIgnoreCase);
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = isHttps,
            SameSite = SameSiteMode.Lax,
            MaxAge = TimeSpan.FromMinutes(10)
        };

        this.Response.Cookies.Append("oauth_state", state, cookieOptions);

        // Save the frontend origin so we know where to redirect after the callback.
        // Validate against configured CORS origins to prevent open redirect token theft.
        var selfOrigin = $"{this.Request.Scheme}://{this.Request.Host}";
        var origin = selfOrigin;

        var referer = this.Request.Headers.Referer.FirstOrDefault();
        if (!string.IsNullOrEmpty(referer) && Uri.TryCreate(referer, UriKind.Absolute, out var refererUri))
        {
            var refererOrigin = $"{refererUri.Scheme}://{refererUri.Authority}";
            if (this._allowedOrigins.Length > 0
                ? this._allowedOrigins.Any(o => string.Equals(o, refererOrigin, StringComparison.OrdinalIgnoreCase))
                : string.Equals(refererOrigin, selfOrigin, StringComparison.OrdinalIgnoreCase))
            {
                origin = refererOrigin;
            }
        }

        this.Response.Cookies.Append("oauth_origin", origin, cookieOptions);

        // Redirect URI points to the API itself, not the Angular app
        var callbackUri = $"{this.Request.Scheme}://{this.Request.Host}/api/auth/discord/callback";
        var redirectUrl = "https://discordapp.com/api/oauth2/authorize" +
            $"?client_id={this._discordSettings.ClientId}" +
            $"&redirect_uri={Uri.EscapeDataString(callbackUri)}" +
            "&response_type=code" +
            "&scope=identify" +
            $"&state={Uri.EscapeDataString(state)}";

        return this.Redirect(redirectUrl);
    }

    [AllowAnonymous]
    [HttpGet("discord/callback")]
    public async Task<IActionResult> DiscordCallback([FromQuery] string code, [FromQuery] string? state)
    {
        // Derive frontend URL from the request
        var frontendUrl = this.GetFrontendUrl();

        // Validate OAuth state parameter for CSRF protection
        var savedState = this.Request.Cookies["oauth_state"];
        this.Response.Cookies.Delete("oauth_state");

        if (string.IsNullOrEmpty(state) || string.IsNullOrEmpty(savedState) || state != savedState)
        {
            return this.BadRequest(new
            {
                error = "Invalid OAuth state. Possible CSRF attack."
            });
        }

        if (string.IsNullOrEmpty(code))
        {
            return this.Redirect($"{frontendUrl}/login#error=missing_code");
        }

        using var httpClient = new HttpClient();

        // Exchange code for access token
        var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = this._discordSettings.ClientId,
            ["client_secret"] = this._discordSettings.ClientSecret,
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = $"{this.Request.Scheme}://{this.Request.Host}/api/auth/discord/callback"
        });

        var tokenResponse = await httpClient.PostAsync("https://discordapp.com/api/oauth2/token", tokenRequest);
        if (!tokenResponse.IsSuccessStatusCode)
        {
            var errorBody = await tokenResponse.Content.ReadAsStringAsync();
            LogDiscordTokenExchangeFailed(this._logger, tokenResponse.StatusCode, errorBody);
            return this.Redirect($"{frontendUrl}/login#error=token_exchange_failed");
        }

        var tokenJson = await tokenResponse.Content.ReadFromJsonAsync<JsonElement>();
        var accessToken = tokenJson.GetProperty("access_token").GetString();

        // Get Discord user info
        httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        var userResponse = await httpClient.GetAsync("https://discordapp.com/api/users/@me");
        if (!userResponse.IsSuccessStatusCode)
        {
            return this.Redirect($"{frontendUrl}/login#error=discord_user_fetch_failed");
        }

        var discordUser = await userResponse.Content.ReadFromJsonAsync<JsonElement>();
        var discordId = discordUser.GetProperty("id").GetString()!;
        var username = discordUser.GetProperty("username").GetString()!;
        var avatar = discordUser.TryGetProperty("avatar", out var avatarProp) ? avatarProp.GetString() : null;
        var avatarUrl = avatar != null
            ? $"https://cdn.discordapp.com/avatars/{discordId}/{avatar}.png"
            : null;

        // Look up user in DB
        var human = await this._humanService.GetByIdAsync(discordId);
        if (human == null)
        {
            return this.Redirect($"{frontendUrl}/login#error=user_not_registered");
        }

        // Role-based access: check Discord guild roles if enabled
        var (isAdmin, managedWebhooks) = await this.GetRolesAsync(discordId);
        if (!isAdmin)
        {
            // Enforce enable_discord site setting for non-admin users.
            // Admins can always log in so they can re-enable the setting.
            var discordSetting = await this._siteSettingService.GetValueAsync(EnableDiscordKey);
            if (string.Equals(discordSetting, "false", StringComparison.OrdinalIgnoreCase))
            {
                LogAuthMethodDisabled(this._logger, "Discord");
                return this.Redirect($"{frontendUrl}/login#error=discord_disabled");
            }

            var roleCheckResult = await this.CheckRoleAccessAsync(discordId);
            if (roleCheckResult != null)
            {
                return this.Redirect($"{frontendUrl}/login#error={roleCheckResult}");
            }
        }

        var userInfo = new UserInfo
        {
            Id = discordId,
            Username = username,
            Type = "discord:user",
            IsAdmin = isAdmin,
            AdminDisable = human.AdminDisable == 1,
            Enabled = human.Enabled == 1 && human.AdminDisable == 0,
            ProfileNo = human.CurrentProfileNo,
            AvatarUrl = avatarUrl,
            ManagedWebhooks = managedWebhooks
        };

        // Cache avatar for admin panel use
        if (!string.IsNullOrEmpty(avatarUrl))
        {
            Services.AvatarCacheService.SetAvatar(discordId, avatarUrl);
            Services.AvatarCacheService.Save();
        }

        var jwt = this._jwtService.GenerateToken(userInfo);

        // Redirect browser to Angular with token in URL fragment to avoid server-side leakage
        return this.Redirect($"{frontendUrl}/auth/discord/callback#token={jwt}");
    }

    [AllowAnonymous]
    [HttpGet("oidc/login")]
    public IActionResult OidcLogin()
    {
        // Generic external OIDC/OAuth2 provider — a configurable twin of the Discord flow.
        // No early enable_oidc gate here: admins must be able to log in even when the
        // provider is disabled for regular users. The check runs in OidcCallback() once
        // we know whether the user is an admin (mirrors Discord).
        if (!this.OidcConfigured())
        {
            return this.NotFound(new
            {
                error = "External login provider is not configured."
            });
        }

        var state = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        var isHttps = string.Equals(this.Request.Scheme, "https", StringComparison.OrdinalIgnoreCase);
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = isHttps,
            SameSite = SameSiteMode.Lax,
            MaxAge = TimeSpan.FromMinutes(10)
        };

        this.Response.Cookies.Append("oauth_state", state, cookieOptions);

        // Save the frontend origin (validated against CORS origins) so the callback knows
        // where to redirect — identical handling to DiscordLogin.
        var selfOrigin = $"{this.Request.Scheme}://{this.Request.Host}";
        var origin = selfOrigin;

        var referer = this.Request.Headers.Referer.FirstOrDefault();
        if (!string.IsNullOrEmpty(referer) && Uri.TryCreate(referer, UriKind.Absolute, out var refererUri))
        {
            var refererOrigin = $"{refererUri.Scheme}://{refererUri.Authority}";
            if (this._allowedOrigins.Length > 0
                ? this._allowedOrigins.Any(o => string.Equals(o, refererOrigin, StringComparison.OrdinalIgnoreCase))
                : string.Equals(refererOrigin, selfOrigin, StringComparison.OrdinalIgnoreCase))
            {
                origin = refererOrigin;
            }
        }

        this.Response.Cookies.Append("oauth_origin", origin, cookieOptions);

        var callbackUri = $"{this.Request.Scheme}://{this.Request.Host}/api/auth/oidc/callback";

        var query = new Dictionary<string, string?>
        {
            ["client_id"] = this._oidcSettings.ClientId,
            ["redirect_uri"] = callbackUri,
            ["response_type"] = "code",
            ["scope"] = this.BuildOidcScope(),
            ["state"] = state,
        };

        if (this._oidcSettings.UsePkce)
        {
            // PKCE: store the verifier in an HttpOnly cookie and send only the S256 challenge.
            var codeVerifier = Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
            var challenge = Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));
            this.Response.Cookies.Append("oauth_pkce_verifier", codeVerifier, cookieOptions);
            query["code_challenge"] = challenge;
            query["code_challenge_method"] = "S256";
        }

        return this.Redirect(BuildUrlWithQuery(this._oidcSettings.AuthorizationUrl, query));
    }

    [AllowAnonymous]
    [HttpGet("oidc/logout")]
    public async Task<IActionResult> OidcLogout()
    {
        // OIDC RP-initiated (single) logout: bounce the browser to the provider's end-session
        // endpoint so it can clear its OWN session too, then return to the signed-out landing.
        // The frontend has already discarded the local JWT before calling this.
        var selfOrigin = $"{this.Request.Scheme}://{this.Request.Host}";
        var origin = selfOrigin;

        // Validate the return origin the same way the login flow validates oauth_origin.
        var referer = this.Request.Headers.Referer.FirstOrDefault();
        if (!string.IsNullOrEmpty(referer) && Uri.TryCreate(referer, UriKind.Absolute, out var refererUri))
        {
            var refererOrigin = $"{refererUri.Scheme}://{refererUri.Authority}";
            if (this._allowedOrigins.Length > 0
                ? this._allowedOrigins.Any(o => string.Equals(o, refererOrigin, StringComparison.OrdinalIgnoreCase))
                : string.Equals(refererOrigin, selfOrigin, StringComparison.OrdinalIgnoreCase))
            {
                origin = refererOrigin;
            }
        }

        // ?loggedout=1 tells the login page to show the signed-out panel instead of auto-redirecting.
        var postLogout = $"{origin}/login?loggedout=1";

        // Single logout requires both a configured end-session endpoint and the admin
        // runtime toggle (enable_oidc_slo; absent = on). Otherwise fall back to local logout.
        var sloSetting = await this._siteSettingService.GetValueAsync(EnableOidcSloKey);
        var sloDisabledByAdmin = string.Equals(sloSetting, "false", StringComparison.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(this._oidcSettings.EndSessionUrl) || sloDisabledByAdmin)
        {
            return this.Redirect(postLogout);
        }

        var query = new Dictionary<string, string?>
        {
            ["post_logout_redirect_uri"] = postLogout,
            ["client_id"] = this._oidcSettings.ClientId,
        };

        return this.Redirect(BuildUrlWithQuery(this._oidcSettings.EndSessionUrl, query));
    }

    [AllowAnonymous]
    [HttpGet("oidc/callback")]
    public async Task<IActionResult> OidcCallback([FromQuery] string code, [FromQuery] string? state)
    {
        var frontendUrl = this.GetFrontendUrl();

        // Validate OAuth state parameter for CSRF protection
        var savedState = this.Request.Cookies["oauth_state"];
        this.Response.Cookies.Delete("oauth_state");

        var pkceVerifier = this.Request.Cookies["oauth_pkce_verifier"];
        this.Response.Cookies.Delete("oauth_pkce_verifier");

        if (string.IsNullOrEmpty(state) || string.IsNullOrEmpty(savedState) || state != savedState)
        {
            return this.BadRequest(new
            {
                error = "Invalid OAuth state. Possible CSRF attack."
            });
        }

        if (!this.OidcConfigured())
        {
            return this.Redirect($"{frontendUrl}/login#error=oidc_disabled");
        }

        if (string.IsNullOrEmpty(code))
        {
            return this.Redirect($"{frontendUrl}/login#error=missing_code");
        }

        // Exchange the authorization code for tokens via the generic, provider-agnostic OIDC client.
        var redirectUri = $"{this.Request.Scheme}://{this.Request.Host}/api/auth/oidc/callback";
        var tokenResult = await this._oidcClient.ExchangeCodeAsync(code, redirectUri, pkceVerifier);
        if (tokenResult is null)
        {
            return this.Redirect($"{frontendUrl}/login#error=oidc_token_exchange_failed");
        }

        var userInfoJson = await this._oidcClient.GetUserInfoAsync(tokenResult.AccessToken);
        if (userInfoJson is null)
        {
            return this.Redirect($"{frontendUrl}/login#error=oidc_userinfo_failed");
        }

        var (userInfo, error) = await this.BuildOidcUserInfoAsync(userInfoJson.Value);
        if (error is not null || userInfo is null)
        {
            return this.Redirect($"{frontendUrl}/login#error={error ?? "oidc_no_identity"}");
        }

        if (!string.IsNullOrEmpty(userInfo.AvatarUrl))
        {
            Services.AvatarCacheService.SetAvatar(userInfo.Id, userInfo.AvatarUrl);
            Services.AvatarCacheService.Save();
        }

        // When refresh-token consumption is enabled AND the provider actually issued a refresh
        // token, persist an encrypted server-side session and hand the browser a short-lived JWT
        // plus an opaque refresh token. Otherwise fall back to a normal full-lifetime JWT — this is
        // the graceful path for providers that don't issue refresh tokens (or when the feature is off).
        if (this._oidcSettings.UseRefreshTokens && !string.IsNullOrEmpty(tokenResult.RefreshToken))
        {
            var (ip, ua) = this.GetClientMetadata();
            var opaque = await this._oidcSessionService.IssueAsync(userInfo.Id, tokenResult.RefreshToken, ip, ua);
            var shortJwt = this._jwtService.GenerateToken(userInfo, this._oidcSettings.AccessTokenMinutes);
            return this.Redirect($"{frontendUrl}/auth/oidc/callback#token={shortJwt}&refresh_token={opaque}");
        }

        if (this._oidcSettings.UseRefreshTokens)
        {
            LogOidcRefreshUnavailable(this._logger);
        }

        var jwt = this._jwtService.GenerateToken(userInfo);
        return this.Redirect($"{frontendUrl}/auth/oidc/callback#token={jwt}");
    }

    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [HttpPost("oidc/refresh")]
    public async Task<IActionResult> OidcRefresh([FromBody] OidcRefreshRequest request)
    {
        if (!this._oidcSettings.UseRefreshTokens || string.IsNullOrEmpty(request?.RefreshToken))
        {
            return this.Unauthorized(new { error = "invalid_grant" });
        }

        var (ip, ua) = this.GetClientMetadata();

        OidcRotationTicket ticket;
        try
        {
            ticket = await this._oidcSessionService.StartRotationAsync(request.RefreshToken, ip, ua);
        }
        catch (UnauthorizedAccessException)
        {
            return this.Unauthorized(new { error = "invalid_grant" });
        }

        // Redeem the provider refresh token. A failure here means the provider revoked it (or the
        // user was disabled at the provider) — propagate by revoking our family and logging out.
        var tokenResult = await this._oidcClient.RefreshAsync(ticket.DecryptedRefreshToken);
        if (tokenResult is null)
        {
            await this._oidcSessionService.AbortRotationAsync(ticket, "provider_revoked");
            return this.Unauthorized(new { error = "invalid_grant" });
        }

        // Re-validate the user live (existence, enabled, roles) on every refresh.
        var userInfoJson = await this._oidcClient.GetUserInfoAsync(tokenResult.AccessToken);
        if (userInfoJson is null)
        {
            await this._oidcSessionService.AbortRotationAsync(ticket, "provider_revoked");
            return this.Unauthorized(new { error = "invalid_grant" });
        }

        var (userInfo, error) = await this.BuildOidcUserInfoAsync(userInfoJson.Value);
        if (error is not null || userInfo is null)
        {
            await this._oidcSessionService.AbortRotationAsync(ticket, "account_inactive");
            return this.Unauthorized(new { error = "invalid_grant" });
        }

        // Propagate an admin disable: kill the session rather than keep renewing it. (A user who
        // merely toggled their own alerts off keeps AdminDisable == false and is unaffected.)
        if (userInfo.AdminDisable)
        {
            await this._oidcSessionService.AbortRotationAsync(ticket, "admin_disable");
            return this.Unauthorized(new { error = "invalid_grant" });
        }

        // Non-rotating providers return no new refresh token — carry the existing one forward.
        var newIdpRefreshToken = tokenResult.RefreshToken ?? ticket.DecryptedRefreshToken;
        await this._oidcSessionService.CompleteRotationAsync(ticket, newIdpRefreshToken);

        var jwt = this._jwtService.GenerateToken(userInfo, this._oidcSettings.AccessTokenMinutes);

        return this.Ok(new
        {
            token = jwt,
            refreshToken = ticket.NewOpaqueToken,
            expiresIn = this._oidcSettings.AccessTokenMinutes * 60,
        });
    }

    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [HttpPost("oidc/refresh/revoke")]
    public async Task<IActionResult> OidcRefreshRevoke([FromBody] OidcRefreshRequest request)
    {
        if (!string.IsNullOrEmpty(request?.RefreshToken))
        {
            await this._oidcSessionService.RevokeAsync(request.RefreshToken, "logout");
        }

        return this.NoContent();
    }

    [AllowAnonymous]
    [HttpPost("telegram/verify")]
    public async Task<IActionResult> TelegramVerify([FromBody] Dictionary<string, string> telegramData)
    {
        if (telegramData == null || !telegramData.TryGetValue("id", out var telegramId) || !telegramData.TryGetValue("hash", out var hash))
        {
            return this.BadRequest(new
            {
                error = "Invalid Telegram login data."
            });
        }

        if (!this._telegramSettings.Enabled)
        {
            return this.BadRequest(new
            {
                error = "Telegram authentication is not enabled."
            });
        }

        // The enable_telegram site setting (admin runtime toggle) is checked after authentication
        // so that admins can still log in even when Telegram is disabled for regular users.

        // Validate auth_date is not older than 86400 seconds (24 hours)
        if (telegramData.TryGetValue("auth_date", out var authDateStr) &&
            long.TryParse(authDateStr, out var authDateUnix))
        {
            var authDate = DateTimeOffset.FromUnixTimeSeconds(authDateUnix);
            if (DateTimeOffset.UtcNow - authDate > TimeSpan.FromSeconds(86400))
            {
                return this.Unauthorized(new
                {
                    error = "Telegram authentication data has expired."
                });
            }
        }
        else
        {
            return this.BadRequest(new
            {
                error = "Missing or invalid auth_date."
            });
        }

        // Validate HMAC-SHA256
        var botToken = this._telegramSettings.BotToken;

        var dataCheckString = string.Join("\n",
            telegramData
                .Where(kvp => kvp.Key != "hash")
                .OrderBy(kvp => kvp.Key)
                .Select(kvp => $"{kvp.Key}={kvp.Value}"));
        var secretKey = SHA256.HashData(Encoding.UTF8.GetBytes(botToken));

        using var hmac = new HMACSHA256(secretKey);
        var computedHash = Convert.ToHexStringLower(hmac.ComputeHash(Encoding.UTF8.GetBytes(dataCheckString)));

        if (computedHash != hash)
        {
            return this.Unauthorized(new
            {
                error = "Invalid Telegram authentication data."
            });
        }

        var username = telegramData.GetValueOrDefault("username",
            telegramData.GetValueOrDefault("first_name", "Unknown"));
        var photoUrl = telegramData.GetValueOrDefault("photo_url");

        // Look up user in DB
        var human = await this._humanService.GetByIdAsync(telegramId);
        if (human == null)
        {
            return this.StatusCode(403, new
            {
                error = "User not registered in Poracle."
            });
        }

        var (isAdmin, managedWebhooks) = await this.GetRolesAsync(telegramId);

        // Enforce enable_telegram site setting for non-admin users.
        // Admins can always log in so they can re-enable the setting.
        if (!isAdmin)
        {
            var telegramSetting = await this._siteSettingService.GetValueAsync(EnableTelegramKey);
            if (string.Equals(telegramSetting, "false", StringComparison.OrdinalIgnoreCase))
            {
                LogAuthMethodDisabled(this._logger, "Telegram");
                return this.BadRequest(new
                {
                    error = "Telegram login is disabled for non-admin users."
                });
            }
        }

        var userInfo = new UserInfo
        {
            Id = telegramId,
            Username = username!,
            Type = "telegram:user",
            IsAdmin = isAdmin,
            AdminDisable = human.AdminDisable == 1,
            Enabled = human.Enabled == 1 && human.AdminDisable == 0,
            ProfileNo = human.CurrentProfileNo,
            AvatarUrl = photoUrl,
            ManagedWebhooks = managedWebhooks
        };

        var jwt = this._jwtService.GenerateToken(userInfo);

        return this.Ok(new
        {
            token = jwt,
            user = userInfo
        });
    }

    [AllowAnonymous]
    [HttpGet("telegram/config")]
    public async Task<IActionResult> TelegramConfig()
    {
        // Combines PoracleNG server config (Telegram:Enabled, requires restart) with
        // PoracleWeb.NET site setting (enable_telegram, runtime toggle). Both must be
        // truthy for Telegram login to be available. Neither affects PoracleNG's Telegram
        // bot or DM delivery — only login to this web UI.
        var telegramSetting = await this._siteSettingService.GetValueAsync(EnableTelegramKey);
        var disabledBySetting = string.Equals(telegramSetting, "false", StringComparison.OrdinalIgnoreCase);

        return this.Ok(new
        {
            enabled = this._telegramSettings.Enabled && !disabledBySetting,
            botUsername = this._telegramSettings.BotUsername
        });
    }

    /// <summary>
    /// Returns auth provider availability for the login page. Combines server-side
    /// .env/appsettings configuration ("configured") with admin-togglable site
    /// settings ("enabled"). The login page uses this to decide which buttons to
    /// render and whether to show a "disabled by admin" message.
    /// </summary>
    [AllowAnonymous]
    [EnableRateLimiting("auth-read")]
    [HttpGet("providers")]
    public async Task<IActionResult> Providers()
    {
        // Discord is "configured" if the server has ClientId and ClientSecret (validated at startup).
        var discordConfigured = !string.IsNullOrEmpty(this._discordSettings.ClientId)
                                && !string.IsNullOrEmpty(this._discordSettings.ClientSecret);

        // Telegram is "configured" if Telegram:Enabled is true in .env/appsettings
        // (auto-inferred by Program.cs when bot credentials are present).
        var telegramConfigured = this._telegramSettings.Enabled;

        // Admin can disable either provider at runtime via site_settings without restart.
        // Absent/null = enabled (safe default — prevents lockout on first-time setup).
        // Sequential awaits — EF Core DbContext is not thread-safe so Task.WhenAll is not viable.
        var discordSetting = await this._siteSettingService.GetValueAsync(EnableDiscordKey);
        var discordDisabledByAdmin = string.Equals(discordSetting, "false", StringComparison.OrdinalIgnoreCase);

        var telegramSetting = await this._siteSettingService.GetValueAsync(EnableTelegramKey);
        var telegramDisabledByAdmin = string.Equals(telegramSetting, "false", StringComparison.OrdinalIgnoreCase);

        // Generic external OIDC provider — "configured" requires the full server-side config.
        // Unlike Discord/Telegram (absent setting = enabled), OIDC is OPT-IN: it is only
        // active when enable_oidc is explicitly "true", so the default sign-in mode is local.
        // The AUTH_FORCE_LOCAL break-glass env flag forces it off regardless — recovery when
        // an admin switches to OIDC against a broken provider and locks everyone out.
        var oidcConfigured = this.OidcConfigured();
        var oidcSetting = await this._siteSettingService.GetValueAsync(EnableOidcKey);
        var forceLocal = configuration.GetValue<bool>("Auth:ForceLocal");
        var oidcEnabledByAdmin = string.Equals(oidcSetting, "true", StringComparison.OrdinalIgnoreCase) && !forceLocal;

        // Single logout: available when a provider end-session endpoint is configured AND the
        // admin runtime toggle is on (enable_oidc_slo; absent = on once the URL is wired).
        var endSessionConfigured = !string.IsNullOrWhiteSpace(this._oidcSettings.EndSessionUrl);
        var sloSetting = await this._siteSettingService.GetValueAsync(EnableOidcSloKey);
        var sloEnabledByAdmin = !string.Equals(sloSetting, "false", StringComparison.OrdinalIgnoreCase);

        // Silent refresh is active when the provider is configured and the server-side master
        // switch (OIDC_USE_REFRESH_TOKENS) is on. Read-only status — there is intentionally no
        // runtime admin override: enabling/disabling refresh is a deploy-time decision because it
        // is coupled to the per-login JWT lifetime (turning it off mid-session would strand the
        // short-lived JWTs of already-logged-in users). Single logout (above) is different — it
        // only affects the next logout, so it stays a runtime toggle.
        var refreshConfigured = oidcConfigured && this._oidcSettings.UseRefreshTokens;

        return this.Ok(new
        {
            discord = new
            {
                configured = discordConfigured,
                enabledByAdmin = !discordDisabledByAdmin,
            },
            telegram = new
            {
                configured = telegramConfigured,
                enabledByAdmin = !telegramDisabledByAdmin,
                botUsername = telegramConfigured ? this._telegramSettings.BotUsername : string.Empty,
            },
            oidc = new
            {
                configured = oidcConfigured,
                enabledByAdmin = oidcEnabledByAdmin,
                providerName = oidcConfigured ? this._oidcSettings.ProviderName : string.Empty,
                // Whether single logout ("Sign out everywhere") is available: end-session
                // endpoint configured AND enabled by the admin (enable_oidc_slo).
                endSession = oidcConfigured && endSessionConfigured && sloEnabledByAdmin,
                // Whether the frontend should run silent refresh (server brokers the provider RT).
                refresh = refreshConfigured,
            },
        });
    }

    [EnableRateLimiting("auth-read")]
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        // Read enabled status from DB (not JWT) so it reflects real-time changes
        var human = await this._humanService.GetByIdAsync(this.UserId);
        var adminDisable = human != null && human.AdminDisable == 1;
        var enabled = human == null || (human.Enabled == 1 && human.AdminDisable == 0);
        var dbProfileNo = human?.CurrentProfileNo ?? this.ProfileNo;

        var userInfo = new UserInfo
        {
            Id = this.UserId,
            Username = this.Username,
            Type = this.User.FindFirstValue("type") ?? string.Empty,
            IsAdmin = this.IsAdmin,
            AdminDisable = adminDisable,
            Enabled = enabled,
            ProfileNo = dbProfileNo,
            AvatarUrl = this.User.FindFirstValue("avatarUrl"),
            ManagedWebhooks = this.ManagedWebhooks.Length > 0 ? this.ManagedWebhooks : null
        };

        // Detect JWT/DB profile desync — PoracleNG can change current_profile_no
        // out-of-band via the active_hours scheduler or bot !profile commands.
        // When mismatched, issue a refreshed JWT so subsequent API calls use the
        // correct profile and alarms don't land on the wrong profile.
        if (human != null && dbProfileNo != this.ProfileNo)
        {
            LogProfileResync(this._logger, this.UserId, this.ProfileNo, dbProfileNo);
            userInfo.Token = this._jwtService.GenerateToken(userInfo);
        }

        return this.Ok(userInfo);
    }

    [EnableRateLimiting("auth-read")]
    [HttpPost("alerts/toggle")]
    public async Task<IActionResult> ToggleAlerts()
    {
        var human = await this._humanService.GetByIdAsync(this.UserId);
        if (human == null)
        {
            return this.NotFound();
        }

        if (human.AdminDisable == 1)
        {
            return this.BadRequest(new
            {
                error = "Your account has been disabled by an administrator."
            });
        }

        var newEnabled = human.Enabled != 1;
        if (newEnabled)
        {
            await this._humanProxy.StartAsync(this.UserId);
        }
        else
        {
            await this._humanProxy.StopAsync(this.UserId);
        }

        return this.Ok(new
        {
            enabled = newEnabled
        });
    }

    [EnableRateLimiting("auth-read")]
    [HttpPost("logout")]
    public IActionResult Logout() => this.Ok(new
    {
        message = "Logged out successfully."
    });

    /// <summary>
    /// Calls PoracleJS getAdministrationRoles once and returns (isAdmin, managedWebhooks).
    /// managedWebhooks merges: Poracle-resolved webhook delegation + our own webhook delegate service layer.
    /// </summary>
    private async Task<(bool isAdmin, string[]? managedWebhooks)> GetRolesAsync(string userId)
    {
        // Fast path: configured admin IDs
        if (!string.IsNullOrEmpty(this._poracleSettings.AdminIds))
        {
            var adminIds = this._poracleSettings.AdminIds.Split(',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (adminIds.Contains(userId))
            {
                return (true, null);
            }
        }

        // Check Poracle config admins list
        try
        {
            var config = await this._poracleApiProxy.GetConfigAsync();
            if (config?.Admins != null &&
                (config.Admins.Discord.Contains(userId) || config.Admins.Telegram.Contains(userId)))
            {
                return (true, null);
            }
        }
        catch (Exception ex)
        {
            LogPoracleConfigFetchFailed(this._logger, ex, userId);
        }

        // Call getAdministrationRoles once — resolves delegation including Discord guild roles
        var managed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var isAdmin = false;

        try
        {
            var rolesJson = await this._poracleApiProxy.GetAdminRolesAsync(userId);
            if (!string.IsNullOrEmpty(rolesJson))
            {
                using var doc = JsonDocument.Parse(rolesJson);
                var root = doc.RootElement;

                // Some versions return isAdmin at root; others wrap under admin.discord
                if (root.TryGetProperty("isAdmin", out var isAdminProp) && isAdminProp.ValueKind == JsonValueKind.True)
                {
                    isAdmin = true;
                }

                // Parse admin.discord.webhooks — the authoritative delegate webhook list
                if (root.TryGetProperty("admin", out var adminEl) &&
                    adminEl.TryGetProperty("discord", out var discordEl))
                {
                    if (!isAdmin &&
                        discordEl.TryGetProperty("isAdmin", out var discordAdmin) &&
                        discordAdmin.ValueKind == JsonValueKind.True)
                    {
                        isAdmin = true;
                    }

                    if (discordEl.TryGetProperty("webhooks", out var webhooks) &&
                        webhooks.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var wh in webhooks.EnumerateArray())
                        {
                            if (wh.GetString() is { } id)
                            {
                                managed.Add(id);
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogAdminRolesFetchFailed(this._logger, ex, userId);
        }

        if (isAdmin)
        {
            return (true, null);
        }

        // Also merge our own webhook delegate service layer
        try
        {
            var managedWebhookIds = await this._webhookDelegateService.GetManagedWebhookIdsAsync(userId);
            foreach (var webhookId in managedWebhookIds)
            {
                managed.Add(webhookId);
            }
        }
        catch (Exception ex)
        {
            LogPwebDelegatesFetchFailed(this._logger, ex, userId);
        }

        return (false, managed.Count > 0 ? managed.ToArray() : null);
    }

    /// <summary>
    /// Checks if role-based access is enabled and whether the user has an allowed role.
    /// Returns null if access is granted, or an error string if denied.
    /// </summary>
    private async Task<string?> CheckRoleAccessAsync(string discordId)
    {
        try
        {
            // Check if role-based access is enabled
            var enableRoles = await this._siteSettingService.GetBoolAsync("enable_roles");
            if (!enableRoles)
            {
                return null; // Not enabled, allow access
            }

            // Get allowed role IDs
            var roleIdsStr = await this._siteSettingService.GetValueAsync("allowed_role_ids");
            if (string.IsNullOrWhiteSpace(roleIdsStr))
            {
                return null; // No roles configured, allow everyone
            }

            var allowedRoles = new HashSet<string>(
                roleIdsStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

            // Need bot token and guild ID to check roles
            if (string.IsNullOrEmpty(this._discordSettings.BotToken) || string.IsNullOrEmpty(this._discordSettings.GuildId))
            {
                LogRoleMisconfigured(this._logger);
                return null; // Misconfigured, fail open
            }

            // Fetch user's guild member data via bot API
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bot", this._discordSettings.BotToken);

            var response = await httpClient.GetAsync(
                $"https://discordapp.com/api/guilds/{this._discordSettings.GuildId}/members/{discordId}");

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                LogGuildMemberFetchFailed(this._logger, discordId, response.StatusCode, errorBody);
                // 404 = not in guild, 403 = bot doesn't have access
                return response.StatusCode == System.Net.HttpStatusCode.NotFound
                    ? "not_in_guild"
                    : "role_check_failed";
            }

            var memberJson = await response.Content.ReadFromJsonAsync<JsonElement>();
            var userRoles = new HashSet<string>();
            if (memberJson.TryGetProperty("roles", out var rolesArray) &&
                rolesArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var role in rolesArray.EnumerateArray())
                {
                    if (role.GetString() is { } roleId)
                    {
                        userRoles.Add(roleId);
                    }
                }
            }

            if (this._logger.IsEnabled(LogLevel.Information))
            {
#pragma warning disable CA1873 // args are only evaluated inside IsEnabled guard
                LogRoleCheck(this._logger, discordId, string.Join(", ", allowedRoles), string.Join(", ", userRoles));
#pragma warning restore CA1873
            }

            // User must have ALL of the allowed roles
            if (allowedRoles.IsSubsetOf(userRoles))
            {
                return null;
            }

            LogRoleDenied(this._logger, discordId);
            return "missing_required_role";
        }
        catch (Exception ex)
        {
            LogRoleCheckFailed(this._logger, ex, discordId);
            return "role_check_failed";
        }
    }

    private string GetFrontendUrl()
    {
        // Use the origin saved during the login step
        var savedOrigin = this.Request.Cookies["oauth_origin"];
        this.Response.Cookies.Delete("oauth_origin");
        if (!string.IsNullOrEmpty(savedOrigin))
        {
            return savedOrigin.TrimEnd('/');
        }

        // Fallback: same scheme/host as the request
        return $"{this.Request.Scheme}://{this.Request.Host}";
    }

    /// <summary>
    /// The external OIDC provider is "configured" only when enabled and the full set of
    /// endpoints plus a client id is present. Mirrors the Discord "configured" check.
    /// </summary>
    private bool OidcConfigured() =>
        this._oidcSettings.Enabled
        && !string.IsNullOrWhiteSpace(this._oidcSettings.AuthorizationUrl)
        && !string.IsNullOrWhiteSpace(this._oidcSettings.TokenUrl)
        && !string.IsNullOrWhiteSpace(this._oidcSettings.UserInfoUrl)
        && !string.IsNullOrWhiteSpace(this._oidcSettings.ClientId);

    /// <summary>
    /// Builds the authorization-request scope, appending the configured offline-access scope
    /// (default <c>offline_access</c>) when refresh consumption is enabled and it isn't already
    /// present — the one and only scope mutation we perform. Standards-compliant providers gate
    /// refresh-token issuance behind this scope; providers that issue unconditionally (or use a
    /// non-standard mechanism) leave <c>OIDC_OFFLINE_ACCESS_SCOPE</c> empty.
    /// </summary>
    private string BuildOidcScope()
    {
        var scope = string.IsNullOrWhiteSpace(this._oidcSettings.Scopes) ? "openid" : this._oidcSettings.Scopes;

        if (this._oidcSettings.UseRefreshTokens && !string.IsNullOrWhiteSpace(this._oidcSettings.OfflineAccessScope))
        {
            var parts = scope.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (!parts.Contains(this._oidcSettings.OfflineAccessScope, StringComparer.Ordinal))
            {
                scope = $"{scope} {this._oidcSettings.OfflineAccessScope}";
            }
        }

        return scope;
    }

    /// <summary>
    /// Maps OIDC userinfo claims to a <see cref="UserInfo"/>, re-validating identity, registration,
    /// the <c>enable_oidc</c> admin gate, and role access. Returns <c>(user, null)</c> on success or
    /// <c>(null, errorCode)</c> on any failure. Shared by the login callback and the refresh path so
    /// a user disabled/derole'd at the provider is re-evaluated on every silent refresh.
    /// </summary>
    private async Task<(UserInfo? user, string? error)> BuildOidcUserInfoAsync(JsonElement userInfoJson)
    {
        // The identity claim maps to the Poracle human id (a Discord/Telegram id).
        // Fall back to the standard OIDC `sub` claim when the configured claim is absent.
        var identity = GetClaimString(userInfoJson, this._oidcSettings.IdentityClaim)
                       ?? GetClaimString(userInfoJson, "sub");
        if (string.IsNullOrEmpty(identity))
        {
            return (null, "oidc_no_identity");
        }

        var username = GetClaimString(userInfoJson, this._oidcSettings.UsernameClaim) ?? identity;
        var avatarUrl = GetClaimString(userInfoJson, this._oidcSettings.AvatarClaim);

        var human = await this._humanService.GetByIdAsync(identity);
        if (human == null)
        {
            return (null, "user_not_registered");
        }

        var (isAdmin, managedWebhooks) = await this.GetRolesAsync(identity);
        if (!isAdmin)
        {
            // Enforce enable_oidc site setting for non-admin users.
            // Admins can always log in so they can re-enable the setting.
            var oidcSetting = await this._siteSettingService.GetValueAsync(EnableOidcKey);
            if (string.Equals(oidcSetting, "false", StringComparison.OrdinalIgnoreCase))
            {
                LogAuthMethodDisabled(this._logger, "OIDC");
                return (null, "oidc_disabled");
            }

            // Reuse Discord guild role gating when the identity is a Discord id.
            var roleCheckResult = await this.CheckRoleAccessAsync(identity);
            if (roleCheckResult != null)
            {
                return (null, roleCheckResult);
            }
        }

        var userInfo = new UserInfo
        {
            Id = identity,
            Username = username,
            Type = string.IsNullOrEmpty(this._oidcSettings.IdentityType) ? "discord:user" : this._oidcSettings.IdentityType,
            IsAdmin = isAdmin,
            AdminDisable = human.AdminDisable == 1,
            Enabled = human.Enabled == 1 && human.AdminDisable == 0,
            ProfileNo = human.CurrentProfileNo,
            AvatarUrl = avatarUrl,
            ManagedWebhooks = managedWebhooks
        };

        return (userInfo, null);
    }

    /// <summary>Best-effort client IP + user-agent for session audit metadata (not security-bearing).</summary>
    private (string? ip, string? ua) GetClientMetadata()
    {
        var ip = this.HttpContext.Connection.RemoteIpAddress?.ToString();
        var ua = this.Request.Headers.UserAgent.ToString();
        return (ip, string.IsNullOrEmpty(ua) ? null : ua);
    }

    /// <summary>
    /// Reads a UserInfo claim as a string, tolerating both string and numeric JSON values
    /// (e.g. a numeric <c>sub</c>). Returns null when absent or empty.
    /// </summary>
    private static string? GetClaimString(JsonElement userInfo, string claim)
    {
        if (string.IsNullOrEmpty(claim) || !userInfo.TryGetProperty(claim, out var prop))
        {
            return null;
        }

        var value = prop.ValueKind switch
        {
            JsonValueKind.String => prop.GetString(),
            JsonValueKind.Number => prop.GetRawText(),
            _ => null
        };

        return string.IsNullOrEmpty(value) ? null : value;
    }

    /// <summary>Base64url encoding without padding (RFC 7636), for PKCE verifier/challenge.</summary>
    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>
    /// Appends query parameters to a base URL, preserving any existing query string the
    /// admin-configured authorization endpoint may already carry. Values are URL-encoded.
    /// </summary>
    private static string BuildUrlWithQuery(string baseUrl, IDictionary<string, string?> parameters)
    {
        var present = parameters
            .Where(kvp => !string.IsNullOrEmpty(kvp.Value))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        return Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString(baseUrl, present);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Role-based access enabled but Discord BotToken or GuildId not configured.")]
    private static partial void LogRoleMisconfigured(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to fetch guild member {UserId}: {Status} {Body}")]
    private static partial void LogGuildMemberFetchFailed(ILogger logger, string userId, System.Net.HttpStatusCode status, string body);

    [LoggerMessage(Level = LogLevel.Information, Message = "Role check for {UserId}: required=[{Required}], user=[{UserRoles}]")]
    private static partial void LogRoleCheck(ILogger logger, string userId, string required, string userRoles);

    [LoggerMessage(Level = LogLevel.Information, Message = "User {UserId} denied: missing required roles.")]
    private static partial void LogRoleDenied(ILogger logger, string userId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Role check failed for {UserId}, denying access.")]
    private static partial void LogRoleCheckFailed(ILogger logger, Exception ex, string userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Discord token exchange failed: {Status} {Body}")]
    private static partial void LogDiscordTokenExchangeFailed(ILogger logger, System.Net.HttpStatusCode status, string body);

    [LoggerMessage(Level = LogLevel.Information, Message = "OIDC refresh tokens are enabled but the provider returned no refresh token (offline_access not granted?); falling back to a standard session.")]
    private static partial void LogOidcRefreshUnavailable(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to fetch Poracle config for admin check for {UserId}.")]
    private static partial void LogPoracleConfigFetchFailed(ILogger logger, Exception ex, string userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to fetch administration roles for {UserId}.")]
    private static partial void LogAdminRolesFetchFailed(ILogger logger, Exception ex, string userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to fetch webhook delegates for {UserId}.")]
    private static partial void LogPwebDelegatesFetchFailed(ILogger logger, Exception ex, string userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Auth attempt blocked: {Method} login is disabled by site setting.")]
    private static partial void LogAuthMethodDisabled(ILogger logger, string method);

    [LoggerMessage(Level = LogLevel.Information, Message = "Profile resync for {UserId}: JWT had profile {JwtProfileNo}, DB has {DbProfileNo}. Issuing refreshed token.")]
    private static partial void LogProfileResync(ILogger logger, string userId, int jwtProfileNo, int dbProfileNo);
}
