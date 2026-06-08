using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Pgan.PoracleWebNet.Api.Configuration;

namespace Pgan.PoracleWebNet.Api.Services.Oidc;

/// <summary>
/// Provider-agnostic OIDC HTTP client. See <see cref="IOidcClient"/>. All provider divergences
/// (token-endpoint auth method, optional/non-rotating refresh tokens, missing expires_in) are
/// handled here so callers (login callback + refresh service) share one identical code path.
/// </summary>
public sealed partial class OidcClient(
    HttpClient httpClient,
    IOptions<OidcSettings> oidcSettings,
    ILogger<OidcClient> logger) : IOidcClient
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly OidcSettings _settings = oidcSettings.Value;
    private readonly ILogger<OidcClient> _logger = logger;

    public async Task<OidcTokenResult?> ExchangeCodeAsync(string code, string redirectUri, string? codeVerifier)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
        };

        if (this._settings.UsePkce && !string.IsNullOrEmpty(codeVerifier))
        {
            form["code_verifier"] = codeVerifier;
        }

        return await this.PostTokenAsync(form, "authorization_code");
    }

    public async Task<OidcTokenResult?> RefreshAsync(string refreshToken)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
        };

        return await this.PostTokenAsync(form, "refresh_token");
    }

    public async Task<JsonElement?> GetUserInfoAsync(string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, this._settings.UserInfoUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await this._httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            LogUserInfoFailed(this._logger, response.StatusCode, body);
            return null;
        }

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>
    /// Posts a token request, applying the configured client-authentication method, and parses
    /// the standard OAuth2 token response. Returns null on transport/HTTP error or a missing
    /// access_token.
    /// </summary>
    private async Task<OidcTokenResult?> PostTokenAsync(Dictionary<string, string> form, string grant)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, this._settings.TokenUrl);

        // Identify the client. With client_secret_basic the secret rides in the Authorization
        // header; with client_secret_post both id and secret go in the body.
        form["client_id"] = this._settings.ClientId;

        if (string.Equals(this._settings.TokenEndpointAuthMethod, "client_secret_basic", StringComparison.OrdinalIgnoreCase))
        {
            var credentials = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{this._settings.ClientId}:{this._settings.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        }
        else
        {
            form["client_secret"] = this._settings.ClientSecret;
        }

        request.Content = new FormUrlEncodedContent(form);

        using var response = await this._httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            LogTokenFailed(this._logger, grant, response.StatusCode, body);
            return null;
        }

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        if (!json.TryGetProperty("access_token", out var accessTokenProp) ||
            accessTokenProp.GetString() is not { Length: > 0 } accessToken)
        {
            LogTokenFailed(this._logger, grant, response.StatusCode, "response had no access_token");
            return null;
        }

        var refreshToken = json.TryGetProperty("refresh_token", out var rtProp) ? rtProp.GetString() : null;
        int? expiresIn = json.TryGetProperty("expires_in", out var expProp) && expProp.ValueKind == JsonValueKind.Number
            ? expProp.GetInt32()
            : null;

        return new OidcTokenResult(accessToken, string.IsNullOrEmpty(refreshToken) ? null : refreshToken, expiresIn);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "OIDC {Grant} token request failed: {Status} {Body}")]
    private static partial void LogTokenFailed(ILogger logger, string grant, System.Net.HttpStatusCode status, string body);

    [LoggerMessage(Level = LogLevel.Warning, Message = "OIDC userinfo fetch failed: {Status} {Body}")]
    private static partial void LogUserInfoFailed(ILogger logger, System.Net.HttpStatusCode status, string body);
}
