using System.Text.Json;
using Microsoft.AspNetCore.Http;
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
/// A site may run without a Discord application (Telegram-only or OIDC-only). The Discord routes must
/// then send the browser back to the login page with a reason, never build an authorize URL with an
/// empty client_id or call Discord's token endpoint.
/// </summary>
public class AuthControllerDiscordUnconfiguredTests
{
    private const string SelfOrigin = "https://alerts.example.net";

    private readonly Mock<IHumanService> _humanService = new();
    private readonly Mock<ISiteSettingService> _siteSettingService = new();

    private AuthController CreateController(DiscordSettings discord, string? stateCookie = null)
    {
        var controller = new AuthController(
            this._humanService.Object,
            new Mock<IProfileService>().Object,
            new Mock<IPoracleApiProxy>().Object,
            new Mock<IPoracleHumanProxy>().Object,
            this._siteSettingService.Object,
            new Mock<IWebhookDelegateService>().Object,
            new Mock<IJwtService>().Object,
            new Mock<Pgan.PoracleWebNet.Api.Services.IUserRoleResolver>().Object,
            new Mock<IOidcClient>().Object,
            new Mock<IOidcSessionService>().Object,
            Options.Create(discord),
            Options.Create(new TelegramSettings { Enabled = true, BotToken = "123:abc", BotUsername = "bot" }),
            Options.Create(new OidcSettings()),
            Options.Create(new PoracleSettings()),
            new ConfigurationBuilder().Build(),
            new Mock<ILogger<AuthController>>().Object);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "https";
        httpContext.Request.Host = new HostString("alerts.example.net");
        if (stateCookie != null)
        {
            httpContext.Request.Headers.Cookie = $"oauth_state={Uri.EscapeDataString(stateCookie)}";
        }

        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("123456789012345678", "")]
    [InlineData("", "secret")]
    public async Task DiscordLoginRedirectsToTheLoginErrorWhenDiscordIsNotConfigured(string clientId, string clientSecret)
    {
        var controller = this.CreateController(new DiscordSettings { ClientId = clientId, ClientSecret = clientSecret });

        var result = await controller.DiscordLogin();

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal($"{SelfOrigin}/login#error=discord_not_configured", redirect.Url);
        Assert.DoesNotContain("discordapp.com", redirect.Url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DiscordLoginStillGoesToDiscordWhenConfigured()
    {
        var controller = this.CreateController(new DiscordSettings { ClientId = "123456789012345678", ClientSecret = "secret" });

        var result = await controller.DiscordLogin();

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.StartsWith("https://discordapp.com/api/oauth2/authorize?client_id=123456789012345678&", redirect.Url, StringComparison.Ordinal);
    }

    /// <summary>
    /// A valid state and a code: without the guard this request would go on to POST to Discord's token
    /// endpoint with an empty client_id. It must stop before any HTTP call and before any user lookup.
    /// </summary>
    [Fact]
    public async Task DiscordCallbackRedirectsToTheLoginErrorWithoutCallingDiscord()
    {
        var controller = this.CreateController(new DiscordSettings(), stateCookie: "state-value");

        var result = await controller.DiscordCallback("auth-code", "state-value");

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal($"{SelfOrigin}/login#error=discord_not_configured", redirect.Url);
        this._humanService.VerifyNoOtherCalls();
        this._siteSettingService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ProvidersReportsDiscordUnconfiguredOnATelegramOnlySite()
    {
        var controller = this.CreateController(new DiscordSettings());

        var result = await controller.Providers();

        var ok = Assert.IsType<OkObjectResult>(result);
        var doc = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        Assert.False(doc.RootElement.GetProperty("discord").GetProperty("configured").GetBoolean());
        Assert.True(doc.RootElement.GetProperty("telegram").GetProperty("configured").GetBoolean());
    }
}
