using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Pgan.PoracleWebNet.Api.Configuration;
using Pgan.PoracleWebNet.Api.Controllers;
using Pgan.PoracleWebNet.Api.Services;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Tests.Helpers;

namespace Pgan.PoracleWebNet.Tests.Controllers;

/// <summary>
/// Tests for AuthController.TelegramVerify: the refusals carry a machine-readable code so the login page
/// can say which one it was, and a blocked account is refused here the way /api/auth/me refuses it. See #1027.
/// </summary>
public class AuthControllerTelegramVerifyTests : ControllerTestBase
{
    private const string BotToken = "123456:test-token";
    private const string TelegramId = "200000001";

    private readonly Mock<IHumanService> _humanService = new();
    private readonly Mock<IJwtService> _jwtService = new();
    private readonly Mock<IUserRoleResolver> _roleResolver = new();
    private readonly Mock<ISiteSettingService> _siteSettingService = new();
    private readonly AuthController _sut;

    public AuthControllerTelegramVerifyTests()
    {
        this._jwtService.Setup(j => j.GenerateToken(It.IsAny<UserInfo>())).Returns("jwt");
        this._roleResolver.Setup(r => r.ResolveAsync(It.IsAny<string>())).ReturnsAsync(new UserRoles(false, null));

        this._sut = new AuthController(
            this._humanService.Object, new Mock<IProfileService>().Object,
            new Mock<IPoracleApiProxy>().Object,
            new Mock<IPoracleHumanProxy>().Object,
            PairTestSupport.UnlinkedWriter(),
            this._siteSettingService.Object,
            new Mock<IWebhookDelegateService>().Object,
            this._jwtService.Object,
            this._roleResolver.Object,
            new Mock<Pgan.PoracleWebNet.Api.Services.Oidc.IOidcClient>().Object,
            new Mock<Pgan.PoracleWebNet.Api.Services.Oidc.IOidcSessionService>().Object,
            Options.Create(new DiscordSettings()),
            Options.Create(new TelegramSettings { Enabled = true, BotToken = BotToken }),
            Options.Create(new OidcSettings()),
            Options.Create(new PoracleSettings()),
            new ConfigurationBuilder().Build(),
            new Mock<IAccountLinkService>().Object,
            new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider(),
            new Mock<IHttpClientFactory>().Object,
            new Mock<ILogger<AuthController>>().Object);
    }

    [Fact]
    public async Task AnUnregisteredUserIsRefusedWithTheNotRegisteredCode()
    {
        this._humanService.Setup(h => h.GetByIdAsync(TelegramId)).ReturnsAsync((Human?)null);

        var result = Assert.IsType<ObjectResult>(await this._sut.TelegramVerify(Signed()));

        Assert.Equal(403, result.StatusCode);
        Assert.Equal("user_not_registered", GetProp(result.Value, "code"));
    }

    [Fact]
    public async Task TheDisabledForNonAdminsRefusalCarriesItsCode()
    {
        this._humanService.Setup(h => h.GetByIdAsync(TelegramId)).ReturnsAsync(new Human { Id = TelegramId, Type = "telegram:user", Enabled = 1 });
        this._siteSettingService.Setup(s => s.GetValueAsync("enable_telegram")).ReturnsAsync("false");

        var result = Assert.IsType<BadRequestObjectResult>(await this._sut.TelegramVerify(Signed()));

        Assert.Equal("telegram_disabled", GetProp(result.Value, "code"));
    }

    [Fact]
    public async Task ABlockedAccountIsRefusedWithTheAccountDisabledCodeAndSupportUrl()
    {
        this._humanService.Setup(h => h.GetByIdAsync(TelegramId)).ReturnsAsync(new Human { Id = TelegramId, Type = "telegram:user", Enabled = 1, AdminDisable = 1 });
        this._siteSettingService.Setup(s => s.GetValueAsync("support_url")).ReturnsAsync("https://example.com/support");

        var result = Assert.IsType<UnauthorizedObjectResult>(await this._sut.TelegramVerify(Signed()));

        Assert.Equal("account_disabled", GetProp(result.Value, "code"));
        Assert.Equal("https://example.com/support", GetProp(result.Value, "supportUrl"));
        this._jwtService.Verify(j => j.GenerateToken(It.IsAny<UserInfo>()), Times.Never);
    }

    [Fact]
    public async Task ABlockedAccountWithNoSupportUrlCarriesNull()
    {
        this._humanService.Setup(h => h.GetByIdAsync(TelegramId)).ReturnsAsync(new Human { Id = TelegramId, Type = "telegram:user", Enabled = 1, AdminDisable = 1 });
        this._siteSettingService.Setup(s => s.GetValueAsync("support_url")).ReturnsAsync((string?)null);

        var result = Assert.IsType<UnauthorizedObjectResult>(await this._sut.TelegramVerify(Signed()));

        Assert.Null(GetProp(result.Value, "supportUrl"));
    }

    [Fact]
    public async Task ARegisteredEnabledUserStillGetsAToken()
    {
        this._humanService.Setup(h => h.GetByIdAsync(TelegramId)).ReturnsAsync(new Human { Id = TelegramId, Type = "telegram:user", Enabled = 1, AdminDisable = 0 });

        var result = Assert.IsType<OkObjectResult>(await this._sut.TelegramVerify(Signed()));

        Assert.Equal("jwt", GetProp(result.Value, "token"));
    }

    [Fact]
    public async Task ADestinationRowKeepsItsOwnCode()
    {
        this._humanService.Setup(h => h.GetByIdAsync(TelegramId)).ReturnsAsync(new Human { Id = TelegramId, Type = "telegram:group", Enabled = 1 });

        var result = Assert.IsType<ObjectResult>(await this._sut.TelegramVerify(Signed()));

        Assert.Equal("destination_account", GetProp(result.Value, "code"));
    }

    private static object? GetProp(object? value, string name) => value?.GetType().GetProperty(name)?.GetValue(value);

    private static Dictionary<string, string> Signed()
    {
        var data = new Dictionary<string, string>
        {
            ["id"] = TelegramId,
            ["first_name"] = "Riverside",
            ["username"] = "riverside_rider",
            ["auth_date"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
        };
        var check = string.Join("\n", data.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k => $"{k.Key}={k.Value}"));
        using var hmac = new HMACSHA256(SHA256.HashData(Encoding.UTF8.GetBytes(BotToken)));
        data["hash"] = Convert.ToHexStringLower(hmac.ComputeHash(Encoding.UTF8.GetBytes(check)));
        return data;
    }
}
