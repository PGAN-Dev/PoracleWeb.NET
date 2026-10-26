using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Pgan.PoracleWebNet.Api.Configuration;
using Pgan.PoracleWebNet.Api.Controllers;
using Pgan.PoracleWebNet.Api.Services;
using Pgan.PoracleWebNet.Api.Services.Oidc;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Tests.Helpers;

namespace Pgan.PoracleWebNet.Tests.Controllers;

/// <summary>
/// The OIDC login and refresh paths refuse a Poracle row that is a channel or webhook rather than a
/// person. See #1025.
/// </summary>
public class AuthControllerOidcTests : ControllerTestBase
{
    private const string Frontend = "https://app.example";

    private readonly Mock<IHumanService> _humanService = new();
    private readonly Mock<IJwtService> _jwtService = new();
    private readonly Mock<IUserRoleResolver> _roleResolver = new();
    private readonly Mock<ISiteSettingService> _siteSettingService = new();
    private readonly Mock<IOidcClient> _oidcClient = new();
    private readonly Mock<IOidcSessionService> _oidcSessions = new();

    public AuthControllerOidcTests()
    {
        this._jwtService.Setup(j => j.GenerateToken(It.IsAny<UserInfo>())).Returns("jwt");
        this._roleResolver.Setup(r => r.ResolveAsync(It.IsAny<string>()))
            .ReturnsAsync(new UserRoles(false, null, Resolved: true));
        this._oidcClient.Setup(c => c.ExchangeCodeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync(new OidcTokenResult("access", null, null));
        this._oidcClient.Setup(c => c.GetUserInfoAsync("access"))
            .ReturnsAsync(JsonDocument.Parse("{\"discord_id\":\"123456789\",\"preferred_username\":\"someone\"}").RootElement);
    }

    [Fact]
    public async Task OidcCallbackRefusesADestinationRow()
    {
        this.SetupHuman("discord:channel");
        var sut = this.Build(new OidcSettings { Enabled = true, AuthorizationUrl = "https://idp.example/authorize", TokenUrl = "https://idp.example/token", UserInfoUrl = "https://idp.example/userinfo", ClientId = "client" }, callbackCookies: true);

        var redirect = Assert.IsType<RedirectResult>(await sut.OidcCallback("code", "s"));

        Assert.EndsWith("#error=destination_account", redirect.Url);
    }

    [Fact]
    public async Task OidcCallbackStillSignsInANullTypePerson()
    {
        this.SetupHuman(null);
        var sut = this.Build(new OidcSettings { Enabled = true, AuthorizationUrl = "https://idp.example/authorize", TokenUrl = "https://idp.example/token", UserInfoUrl = "https://idp.example/userinfo", ClientId = "client" }, callbackCookies: true);

        var redirect = Assert.IsType<RedirectResult>(await sut.OidcCallback("code", "s"));

        Assert.StartsWith($"{Frontend}/auth/oidc/callback#token=", redirect.Url);
    }

    [Fact]
    public async Task OidcRefreshEndsADestinationSession()
    {
        this.SetupHuman("webhook");
        var ticket = new OidcRotationTicket
        {
            UserId = "123456789",
            FamilyId = "f",
            FamilyIssuedAt = DateTime.UtcNow,
            DecryptedRefreshToken = "idp-refresh",
            NewOpaqueToken = "new",
            NewTokenHash = "hash",
        };
        this._oidcSessions.Setup(s => s.StartRotationAsync("opaque", It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync(ticket);
        this._oidcClient.Setup(c => c.RefreshAsync("idp-refresh"))
            .ReturnsAsync(new OidcTokenResult("access", null, null));
        var sut = this.Build(new OidcSettings { Enabled = true, UseRefreshTokens = true }, callbackCookies: false);

        var result = await sut.OidcRefresh(new OidcRefreshRequest { RefreshToken = "opaque" });

        Assert.IsType<UnauthorizedObjectResult>(result);
        this._oidcSessions.Verify(s => s.AbortRotationAsync(ticket, "account_inactive"), Times.Once);
        this._oidcSessions.Verify(
            s => s.CompleteRotationAsync(It.IsAny<OidcRotationTicket>(), It.IsAny<string>()), Times.Never);
        this._jwtService.Verify(j => j.GenerateToken(It.IsAny<UserInfo>()), Times.Never);
        this._jwtService.Verify(j => j.GenerateToken(It.IsAny<UserInfo>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task OidcCallbackSignsInATelegramOnlyAccountThroughTheSecondaryClaim()
    {
        this.UserInfo("{\"sub\":\"42\",\"telegram_id\":\"555000111\",\"preferred_username\":\"tg\"}");
        this.SetupHuman("555000111", "telegram:user");
        var sut = this.Build(Settings(secondary: "telegram_id"), callbackCookies: true);

        var redirect = Assert.IsType<RedirectResult>(await sut.OidcCallback("code", "s"));

        Assert.StartsWith($"{Frontend}/auth/oidc/callback#token=", redirect.Url);
        this._jwtService.Verify(
            j => j.GenerateToken(It.Is<UserInfo>(u => u.Id == "555000111" && u.Type == "telegram:user")), Times.Once);
    }

    [Fact]
    public async Task OidcCallbackWithoutASecondaryClaimStillFallsBackToSub()
    {
        this.UserInfo("{\"sub\":\"42\",\"telegram_id\":\"555000111\"}");
        this.SetupHuman("555000111", "telegram:user");
        var sut = this.Build(Settings(secondary: ""), callbackCookies: true);

        var redirect = Assert.IsType<RedirectResult>(await sut.OidcCallback("code", "s"));

        Assert.EndsWith("#error=user_not_registered", redirect.Url);
        this._humanService.Verify(h => h.GetByIdAsync("42"), Times.Once);
    }

    [Fact]
    public async Task OidcCallbackPrefersTheDiscordAccountWhenBothAreRegistered()
    {
        this.UserInfo("{\"discord_id\":\"123456789\",\"telegram_id\":\"555000111\"}");
        this.SetupHuman("123456789", "discord:user");
        this.SetupHuman("555000111", "telegram:user");
        var sut = this.Build(Settings(secondary: "telegram_id"), callbackCookies: true);

        await sut.OidcCallback("code", "s");

        this._jwtService.Verify(
            j => j.GenerateToken(It.Is<UserInfo>(u => u.Id == "123456789" && u.Type == "discord:user")), Times.Once);
    }

    [Fact]
    public async Task OidcCallbackUsesTheTelegramAccountWhenTheDiscordOneIsNotRegistered()
    {
        // A member who links Discord on the provider but only ever registered with the Telegram bot.
        this.UserInfo("{\"discord_id\":\"123456789\",\"telegram_id\":\"555000111\"}");
        this.SetupHuman("555000111", "telegram:user");
        var sut = this.Build(Settings(secondary: "telegram_id"), callbackCookies: true);

        await sut.OidcCallback("code", "s");

        this._jwtService.Verify(
            j => j.GenerateToken(It.Is<UserInfo>(u => u.Id == "555000111" && u.Type == "telegram:user")), Times.Once);
    }

    [Fact]
    public async Task OidcCallbackDoesNotAskDiscordForATelegramAccountsGuildRoles()
    {
        // The Discord role gate cannot find a Telegram id in the guild; the Telegram widget never applied it either.
        this._siteSettingService.Setup(s => s.GetBoolAsync("enable_roles")).ReturnsAsync(true);
        // An unusable role list fails closed without a network call, so a Discord id would be refused here.
        this._siteSettingService.Setup(s => s.GetValueAsync("allowed_role_ids")).ReturnsAsync("not-a-role");
        this.UserInfo("{\"telegram_id\":\"555000111\"}");
        this.SetupHuman("555000111", "telegram:user");
        var sut = this.Build(Settings(secondary: "telegram_id"), callbackCookies: true);

        var redirect = Assert.IsType<RedirectResult>(await sut.OidcCallback("code", "s"));

        Assert.StartsWith($"{Frontend}/auth/oidc/callback#token=", redirect.Url);
    }

    private static OidcSettings Settings(string secondary) => new()
    {
        Enabled = true,
        AuthorizationUrl = "https://idp.example/authorize",
        TokenUrl = "https://idp.example/token",
        UserInfoUrl = "https://idp.example/userinfo",
        ClientId = "client",
        SecondaryIdentityClaim = secondary,
    };

    private void UserInfo(string json) =>
        this._oidcClient.Setup(c => c.GetUserInfoAsync("access")).ReturnsAsync(JsonDocument.Parse(json).RootElement);

    private void SetupHuman(string id, string type) =>
        this._humanService.Setup(h => h.GetByIdAsync(id))
            .ReturnsAsync(new Human { Id = id, Type = type, Enabled = 1, AdminDisable = 0 });

    private AuthController Build(OidcSettings settings, bool callbackCookies)
    {
        var http = new DefaultHttpContext();
        if (callbackCookies)
        {
            http.Request.Headers.Cookie = $"oauth_state=s; oauth_origin={Frontend}";
        }

        return new AuthController(
            this._humanService.Object, new Mock<IProfileService>().Object,
            new Mock<IPoracleApiProxy>().Object,
            new Mock<IPoracleHumanProxy>().Object,
            PairTestSupport.UnlinkedWriter(),
            this._siteSettingService.Object,
            new Mock<IWebhookDelegateService>().Object,
            this._jwtService.Object,
            this._roleResolver.Object,
            this._oidcClient.Object,
            this._oidcSessions.Object,
            Options.Create(new DiscordSettings()),
            Options.Create(new TelegramSettings()),
            Options.Create(settings),
            Options.Create(new PoracleSettings()),
            new ConfigurationBuilder().Build(),
            new Mock<IAccountLinkService>().Object,
            new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider(),
            new Mock<IHttpClientFactory>().Object,
            new Mock<ILogger<AuthController>>().Object)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
        };
    }

    private void SetupHuman(string? type) =>
        this._humanService.Setup(h => h.GetByIdAsync("123456789"))
            .ReturnsAsync(new Human { Id = "123456789", Type = type, Enabled = 1, AdminDisable = 0 });
}
