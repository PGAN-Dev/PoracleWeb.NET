using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Pgan.PoracleWebNet.Api.Configuration;
using Pgan.PoracleWebNet.Api.Controllers;
using Pgan.PoracleWebNet.Api.Services.Oidc;
using Pgan.PoracleWebNet.Core.Abstractions.Services;

namespace Pgan.PoracleWebNet.Tests.Controllers;

/// <summary>
/// Tests for AuthController.Providers() — the unauthenticated login provider availability endpoint.
/// </summary>
public class AuthControllerProvidersTests : ControllerTestBase
{
    private readonly Mock<ISiteSettingService> _siteSettingService = new();
    private readonly IConfiguration _config = new ConfigurationBuilder().Build();

    private AuthController CreateController(DiscordSettings? discord = null, TelegramSettings? telegram = null, OidcSettings? oidc = null, IConfiguration? config = null) => new(
            new Mock<IHumanService>().Object,
            new Mock<IProfileService>().Object,
            new Mock<IPoracleApiProxy>().Object,
            new Mock<IPoracleHumanProxy>().Object,
            this._siteSettingService.Object,
            new Mock<IWebhookDelegateService>().Object,
            new Mock<IJwtService>().Object,
            new Mock<IOidcClient>().Object,
            new Mock<IOidcSessionService>().Object,
            Options.Create(discord ?? new DiscordSettings { ClientId = "test-id", ClientSecret = "test-secret" }),
            Options.Create(telegram ?? new TelegramSettings()),
            Options.Create(oidc ?? new OidcSettings()),
            Options.Create(new PoracleSettings()),
            config ?? this._config,
            new Mock<ILogger<AuthController>>().Object);

    private static IConfiguration ConfigWith(string key, string value) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [key] = value }).Build();

    private static OidcSettings FullyConfiguredOidc() => new()
    {
        Enabled = true,
        ProviderName = "PogoAlerts",
        AuthorizationUrl = "https://idp.example.com/login",
        TokenUrl = "https://idp.example.com/api/oauth/token",
        UserInfoUrl = "https://idp.example.com/api/oauth/userinfo",
        ClientId = "client-id",
        ClientSecret = "client-secret",
    };

    [Fact]
    public async Task ProvidersDiscordConfiguredWhenClientIdAndSecretPresent()
    {
        var controller = this.CreateController();

        var result = await controller.Providers();

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("discord").GetProperty("configured").GetBoolean());
    }

    [Fact]
    public async Task ProvidersDiscordNotConfiguredWhenClientIdMissing()
    {
        var controller = this.CreateController(discord: new DiscordSettings { ClientId = "", ClientSecret = "secret" });

        var result = await controller.Providers();

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("discord").GetProperty("configured").GetBoolean());
    }

    [Fact]
    public async Task ProvidersDiscordEnabledByAdminWhenSettingAbsent()
    {
        this._siteSettingService.Setup(s => s.GetValueAsync("enable_discord")).ReturnsAsync((string?)null);
        var controller = this.CreateController();

        var result = await controller.Providers();

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("discord").GetProperty("enabledByAdmin").GetBoolean());
    }

    [Fact]
    public async Task ProvidersDiscordDisabledByAdminWhenSettingFalse()
    {
        this._siteSettingService.Setup(s => s.GetValueAsync("enable_discord")).ReturnsAsync("false");
        var controller = this.CreateController();

        var result = await controller.Providers();

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("discord").GetProperty("enabledByAdmin").GetBoolean());
    }

    [Fact]
    public async Task ProvidersTelegramConfiguredWhenEnabled()
    {
        var controller = this.CreateController(telegram: new TelegramSettings { Enabled = true, BotUsername = "testbot" });

        var result = await controller.Providers();

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        var doc = JsonDocument.Parse(json);
        var telegram = doc.RootElement.GetProperty("telegram");
        Assert.True(telegram.GetProperty("configured").GetBoolean());
        Assert.Equal("testbot", telegram.GetProperty("botUsername").GetString());
    }

    [Fact]
    public async Task ProvidersTelegramNotConfiguredWhenDisabledInEnv()
    {
        var controller = this.CreateController(telegram: new TelegramSettings { Enabled = false });

        var result = await controller.Providers();

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("telegram").GetProperty("configured").GetBoolean());
    }

    [Fact]
    public async Task ProvidersTelegramDisabledByAdminWhenSettingFalse()
    {
        this._siteSettingService.Setup(s => s.GetValueAsync("enable_telegram")).ReturnsAsync("false");
        var controller = this.CreateController(telegram: new TelegramSettings { Enabled = true, BotUsername = "bot" });

        var result = await controller.Providers();

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        var doc = JsonDocument.Parse(json);
        var telegram = doc.RootElement.GetProperty("telegram");
        Assert.True(telegram.GetProperty("configured").GetBoolean());
        Assert.False(telegram.GetProperty("enabledByAdmin").GetBoolean());
    }

    [Fact]
    public async Task ProvidersTelegramEnabledByAdminWhenSettingAbsent()
    {
        this._siteSettingService.Setup(s => s.GetValueAsync("enable_telegram")).ReturnsAsync((string?)null);
        var controller = this.CreateController(telegram: new TelegramSettings { Enabled = true, BotUsername = "bot" });

        var result = await controller.Providers();

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("telegram").GetProperty("enabledByAdmin").GetBoolean());
    }

    [Fact]
    public async Task ProvidersFirstTimeSetupEmptyDbBothDefaultEnabled()
    {
        // Simulate first-time setup: no rows in site_settings
        this._siteSettingService.Setup(s => s.GetValueAsync(It.IsAny<string>())).ReturnsAsync((string?)null);
        var controller = this.CreateController(
            discord: new DiscordSettings { ClientId = "id", ClientSecret = "secret" },
            telegram: new TelegramSettings { Enabled = true, BotUsername = "bot" });

        var result = await controller.Providers();

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        var doc = JsonDocument.Parse(json);

        // Both should be configured and enabled by admin (absent = enabled)
        Assert.True(doc.RootElement.GetProperty("discord").GetProperty("configured").GetBoolean());
        Assert.True(doc.RootElement.GetProperty("discord").GetProperty("enabledByAdmin").GetBoolean());
        Assert.True(doc.RootElement.GetProperty("telegram").GetProperty("configured").GetBoolean());
        Assert.True(doc.RootElement.GetProperty("telegram").GetProperty("enabledByAdmin").GetBoolean());
    }

    [Fact]
    public async Task ProvidersTelegramBotUsernameEmptyWhenNotConfigured()
    {
        var controller = this.CreateController(telegram: new TelegramSettings { Enabled = false, BotUsername = "secretbot" });

        var result = await controller.Providers();

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        var doc = JsonDocument.Parse(json);
        Assert.Equal(string.Empty, doc.RootElement.GetProperty("telegram").GetProperty("botUsername").GetString());
    }

    [Fact]
    public async Task ProvidersOidcConfiguredWhenEnabledWithFullConfig()
    {
        var controller = this.CreateController(oidc: FullyConfiguredOidc());

        var result = await controller.Providers();

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        var doc = JsonDocument.Parse(json);
        var oidc = doc.RootElement.GetProperty("oidc");
        Assert.True(oidc.GetProperty("configured").GetBoolean());
        Assert.Equal("PogoAlerts", oidc.GetProperty("providerName").GetString());
    }

    [Fact]
    public async Task ProvidersOidcNotConfiguredWhenDisabled()
    {
        var oidc = FullyConfiguredOidc();
        oidc.Enabled = false;
        var controller = this.CreateController(oidc: oidc);

        var result = await controller.Providers();

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        var doc = JsonDocument.Parse(json);
        var node = doc.RootElement.GetProperty("oidc");
        Assert.False(node.GetProperty("configured").GetBoolean());
        // providerName hidden when not configured
        Assert.Equal(string.Empty, node.GetProperty("providerName").GetString());
    }

    [Fact]
    public async Task ProvidersOidcNotConfiguredWhenUrlsMissing()
    {
        var controller = this.CreateController(oidc: new OidcSettings { Enabled = true, ClientId = "id", ProviderName = "X" });

        var result = await controller.Providers();

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("oidc").GetProperty("configured").GetBoolean());
    }

    [Fact]
    public async Task ProvidersOidcDisabledByAdminWhenSettingFalse()
    {
        this._siteSettingService.Setup(s => s.GetValueAsync("enable_oidc")).ReturnsAsync("false");
        var controller = this.CreateController(oidc: FullyConfiguredOidc());

        var result = await controller.Providers();

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        var doc = JsonDocument.Parse(json);
        var node = doc.RootElement.GetProperty("oidc");
        Assert.True(node.GetProperty("configured").GetBoolean());
        Assert.False(node.GetProperty("enabledByAdmin").GetBoolean());
    }

    [Fact]
    public async Task ProvidersOidcDisabledByAdminWhenSettingAbsent()
    {
        // OIDC is opt-in: an absent enable_oidc means local is the default sign-in mode.
        this._siteSettingService.Setup(s => s.GetValueAsync("enable_oidc")).ReturnsAsync((string?)null);
        var controller = this.CreateController(oidc: FullyConfiguredOidc());

        var result = await controller.Providers();

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("oidc").GetProperty("enabledByAdmin").GetBoolean());
    }

    [Fact]
    public async Task ProvidersOidcEnabledByAdminWhenSettingTrue()
    {
        this._siteSettingService.Setup(s => s.GetValueAsync("enable_oidc")).ReturnsAsync("true");
        var controller = this.CreateController(oidc: FullyConfiguredOidc());

        var result = await controller.Providers();

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("oidc").GetProperty("enabledByAdmin").GetBoolean());
    }

    [Fact]
    public async Task ProvidersOidcForceLocalOverridesEnabled()
    {
        // enable_oidc=true but AUTH_FORCE_LOCAL break-glass is set → OIDC reported disabled.
        this._siteSettingService.Setup(s => s.GetValueAsync("enable_oidc")).ReturnsAsync("true");
        var controller = this.CreateController(oidc: FullyConfiguredOidc(), config: ConfigWith("Auth:ForceLocal", "true"));

        var result = await controller.Providers();

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        var doc = JsonDocument.Parse(json);
        var node = doc.RootElement.GetProperty("oidc");
        Assert.True(node.GetProperty("configured").GetBoolean());
        Assert.False(node.GetProperty("enabledByAdmin").GetBoolean());
    }

    [Fact]
    public void OidcLoginReturnsNotFoundWhenProviderNotConfigured()
    {
        var controller = this.CreateController(oidc: new OidcSettings());
        controller.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
        {
            HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext()
        };

        var result = controller.OidcLogin();

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public void OidcLoginRedirectsToProviderWithStateAndPkce()
    {
        var controller = this.CreateController(oidc: FullyConfiguredOidc());
        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        httpContext.Request.Scheme = "https";
        httpContext.Request.Host = new Microsoft.AspNetCore.Http.HostString("alerts.example.com");
        controller.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext { HttpContext = httpContext };

        var result = controller.OidcLogin();

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.StartsWith("https://idp.example.com/login", redirect.Url, StringComparison.Ordinal);
        Assert.Contains("client_id=client-id", redirect.Url, StringComparison.Ordinal);
        Assert.Contains("response_type=code", redirect.Url, StringComparison.Ordinal);
        Assert.Contains("code_challenge=", redirect.Url, StringComparison.Ordinal);
        Assert.Contains("code_challenge_method=S256", redirect.Url, StringComparison.Ordinal);
        Assert.Contains("state=", redirect.Url, StringComparison.Ordinal);

        // CSRF state and PKCE verifier are persisted in cookies for the callback to validate.
        var setCookies = httpContext.Response.Headers["Set-Cookie"].ToString();
        Assert.Contains("oauth_state=", setCookies, StringComparison.Ordinal);
        Assert.Contains("oauth_pkce_verifier=", setCookies, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProvidersOidcEndSessionTrueWhenEndSessionUrlConfigured()
    {
        var oidc = FullyConfiguredOidc();
        oidc.EndSessionUrl = "https://idp.example.com/logout";
        var controller = this.CreateController(oidc: oidc);

        var result = await controller.Providers();

        var ok = Assert.IsType<OkObjectResult>(result);
        var doc = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        Assert.True(doc.RootElement.GetProperty("oidc").GetProperty("endSession").GetBoolean());
    }

    [Fact]
    public async Task ProvidersOidcEndSessionFalseWhenNotConfigured()
    {
        var controller = this.CreateController(oidc: FullyConfiguredOidc());

        var result = await controller.Providers();

        var ok = Assert.IsType<OkObjectResult>(result);
        var doc = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        Assert.False(doc.RootElement.GetProperty("oidc").GetProperty("endSession").GetBoolean());
    }

    private AuthController CreateLogoutController(string? endSessionUrl)
    {
        var oidc = FullyConfiguredOidc();
        oidc.EndSessionUrl = endSessionUrl ?? string.Empty;
        var controller = this.CreateController(oidc: oidc);
        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        httpContext.Request.Scheme = "https";
        httpContext.Request.Host = new Microsoft.AspNetCore.Http.HostString("alerts.example.com");
        controller.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext { HttpContext = httpContext };
        return controller;
    }

    [Fact]
    public async Task OidcLogoutRedirectsToEndSessionWithPostLogoutWhenConfigured()
    {
        var controller = this.CreateLogoutController("https://idp.example.com/logout");

        var result = await controller.OidcLogout();

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.StartsWith("https://idp.example.com/logout", redirect.Url, StringComparison.Ordinal);
        Assert.Contains("post_logout_redirect_uri=", redirect.Url, StringComparison.Ordinal);
        Assert.Contains(Uri.EscapeDataString("https://alerts.example.com/login?loggedout=1"), redirect.Url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OidcLogoutRedirectsToSignedOutLandingWhenNoEndSession()
    {
        var controller = this.CreateLogoutController(endSessionUrl: null);

        var result = await controller.OidcLogout();

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("https://alerts.example.com/login?loggedout=1", redirect.Url);
    }

    [Fact]
    public async Task OidcLogoutFallsBackToLocalWhenSloDisabledByAdmin()
    {
        // End-session is configured, but the admin turned single logout off (enable_oidc_slo=false).
        this._siteSettingService.Setup(s => s.GetValueAsync("enable_oidc_slo")).ReturnsAsync("false");
        var controller = this.CreateLogoutController("https://idp.example.com/logout");

        var result = await controller.OidcLogout();

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("https://alerts.example.com/login?loggedout=1", redirect.Url);
    }

    [Fact]
    public async Task ProvidersOidcEndSessionFalseWhenSloDisabledByAdmin()
    {
        this._siteSettingService.Setup(s => s.GetValueAsync("enable_oidc_slo")).ReturnsAsync("false");
        var oidc = FullyConfiguredOidc();
        oidc.EndSessionUrl = "https://idp.example.com/logout";
        var controller = this.CreateController(oidc: oidc);

        var result = await controller.Providers();

        var ok = Assert.IsType<OkObjectResult>(result);
        var doc = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        Assert.False(doc.RootElement.GetProperty("oidc").GetProperty("endSession").GetBoolean());
    }
}
