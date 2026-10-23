using Microsoft.AspNetCore.DataProtection;
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

namespace Pgan.PoracleWebNet.Tests.Controllers;

public class AuthControllerLinkCallbackTests
{
    private const string Frontend = "https://alerts.example.net";
    private const string DiscordId = "100000000000000001";
    private const string TelegramId = "200000001";

    private readonly Mock<IAccountLinkService> _links = new();
    private readonly Mock<ISiteSettingService> _settings = new();
    private readonly EphemeralDataProtectionProvider _protection = new();

    private static string Location(IActionResult r) => Assert.IsType<RedirectResult>(r).Url;

    private static AccountLink NewLink() => new(1, DiscordId, TelegramId, LinkSide.Telegram, DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow);

    private AuthController Create(string? linkCookie, string? state = "s1")
    {
        var sut = new AuthController(
            new Mock<IHumanService>().Object,
            new Mock<IProfileService>().Object,
            new Mock<IPoracleApiProxy>().Object,
            new Mock<IPoracleHumanProxy>().Object,
            this._settings.Object,
            new Mock<IWebhookDelegateService>().Object,
            new Mock<IJwtService>().Object,
            new Mock<IUserRoleResolver>().Object,
            new Mock<IOidcClient>().Object,
            new Mock<IOidcSessionService>().Object,
            Options.Create(new DiscordSettings { ClientId = "test-id", ClientSecret = "test-secret" }),
            Options.Create(new TelegramSettings()),
            Options.Create(new OidcSettings()),
            Options.Create(new PoracleSettings()),
            new ConfigurationBuilder().Build(),
            this._links.Object,
            this._protection,
            new Mock<ILogger<AuthController>>().Object);
        var http = new DefaultHttpContext();
        http.Request.Scheme = "https";
        http.Request.Host = new HostString("alerts.example.net");
        var cookies = new List<string>();
        if (state != null)
        {
            cookies.Add($"oauth_state={state}");
        }

        if (linkCookie != null)
        {
            cookies.Add($"oauth_link={Uri.EscapeDataString(linkCookie)}");
        }

        http.Request.Headers.Cookie = string.Join("; ", cookies);
        sut.ControllerContext = new ControllerContext { HttpContext = http };
        return sut;
    }

    [Fact]
    public async Task DiscordCallback_WithLinkCookie_LinksAndRedirectsWithoutToken()
    {
        this._links.Setup(l => l.LinkAsync(TelegramId, LinkSide.Discord, DiscordId, "old.town")).ReturnsAsync(NewLink());

        var url = Location(await this.Create(null).CompleteLinkAsync(Frontend, TelegramId, DiscordId, "old.town"));

        Assert.Equal($"{Frontend}/linked-accounts#linked=discord", url);
        Assert.DoesNotContain("token", url, StringComparison.Ordinal);
        this._links.Verify(l => l.LinkAsync(TelegramId, LinkSide.Discord, DiscordId, "old.town"), Times.Once);
    }

    [Fact]
    public async Task DiscordCallback_LinkRefusedByService_RedirectsWithItsCode()
    {
        this._links.Setup(l => l.LinkAsync(It.IsAny<string>(), It.IsAny<LinkSide>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new LinkRefusalException(LinkRefusalCode.Taken, "taken"));

        var url = Location(await this.Create(null).CompleteLinkAsync(Frontend, TelegramId, DiscordId, "x"));

        Assert.Equal($"{Frontend}/linked-accounts#link_error=link_taken", url);
    }

    [Fact]
    public async Task DiscordCallback_LinkRoleCheckFails_RedirectsNotPermitted()
    {
        this._settings.Setup(s => s.GetBoolAsync("enable_roles")).ReturnsAsync(true);
        this._settings.Setup(s => s.GetValueAsync("allowed_role_ids")).ReturnsAsync("not-a-role");

        var url = Location(await this.Create(null).CompleteLinkAsync(Frontend, TelegramId, DiscordId, "x"));

        Assert.Equal($"{Frontend}/linked-accounts#link_error=link_not_permitted", url);
        this._links.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DiscordCallback_DiscordSignInDisabled_RedirectsNotPermitted()
    {
        this._settings.Setup(s => s.GetValueAsync("enable_discord")).ReturnsAsync("false");

        var url = Location(await this.Create(null).CompleteLinkAsync(Frontend, TelegramId, DiscordId, "x"));

        Assert.Equal($"{Frontend}/linked-accounts#link_error=link_not_permitted", url);
        this._links.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DiscordCallback_LinkCookieTampered_RedirectsLinkExpired()
    {
        var sealedCookie = AccountLinkTicket.Seal(this._protection, TelegramId, DateTimeOffset.UtcNow);

        var url = Location(await this.Create(sealedCookie[..^4] + "AAAA").DiscordCallback("code", "s1"));

        Assert.Equal($"{Frontend}/linked-accounts#link_error=link_expired", url);
    }

    [Fact]
    public async Task DiscordCallback_LinkCookieExpired_RedirectsLinkExpired()
    {
        var sealedCookie = AccountLinkTicket.Seal(this._protection, TelegramId, DateTimeOffset.UtcNow.AddMinutes(-11));

        var url = Location(await this.Create(sealedCookie).DiscordCallback("code", "s1"));

        Assert.Equal($"{Frontend}/linked-accounts#link_error=link_expired", url);
    }

    [Fact]
    public async Task DiscordCallback_WithoutLinkCookie_IsTodaysSignIn()
    {
        // No oauth_link: the link branch is not entered, and a missing code is the sign-in's own answer.
        var url = Location(await this.Create(null).DiscordCallback("", "s1"));

        Assert.Equal($"{Frontend}/login#error=missing_code", url);
        this._links.VerifyNoOtherCalls();
    }

    [Fact]
    public void Ticket_ValidCookie_OpensToTheHumanId_AndWrongPurposeDoesNot()
    {
        var sealedCookie = AccountLinkTicket.Seal(this._protection, TelegramId, DateTimeOffset.UtcNow);

        Assert.Equal(TelegramId, AccountLinkTicket.Open(this._protection, sealedCookie, DateTimeOffset.UtcNow));
        var other = this._protection.CreateProtector("Other").Protect("{\"HumanId\":\"x\",\"Expires\":\"2999-01-01T00:00:00+00:00\"}");
        Assert.Null(AccountLinkTicket.Open(this._protection, other, DateTimeOffset.UtcNow));
    }
}
