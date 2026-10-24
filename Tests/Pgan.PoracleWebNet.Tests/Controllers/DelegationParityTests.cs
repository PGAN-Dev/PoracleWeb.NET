using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
/// "Who may manage what" is asked on four surfaces: the My Destinations list, minting an impersonation
/// token, <c>/api/auth/me</c> (which decides whether the nav item renders), and the per-request re-check of
/// an impersonation session. Each break in this area so far was one surface disagreeing with another
/// (#564, #601, #626, #786, #797), so this runs all four over one real <see cref="UserRoleResolver"/>
/// rather than a mock per controller, which could agree with itself and still disagree with the others.
/// </summary>
public class DelegationParityTests : ControllerTestBase
{
    private const string Delegate = "123456789";
    private const string Channel = "c1";

    private readonly Mock<IPoracleApiProxy> _poracleApi = new();
    private readonly Mock<IPoracleHumanProxy> _humanProxy = new();
    private readonly Mock<IWebhookDelegateService> _delegates = new();
    private readonly Mock<IDestinationDirectory> _directory = new();
    private readonly Mock<ISiteSettingService> _siteSettings = new();
    private readonly Mock<IHumanService> _humans = new();
    private readonly Mock<IJwtService> _jwt = new();
    private readonly UserRoleResolver _resolver;

    public DelegationParityTests()
    {
        var channelRow = new Human { Id = Channel, Name = "raids", Type = HumanKind.DiscordChannel, Enabled = 1, CurrentProfileNo = 1 };
        var personRow = new Human { Id = Delegate, Name = "Sam", Type = "discord:user", Enabled = 1, CurrentProfileNo = 1 };

        this._poracleApi.Setup(p => p.GetConfigAsync()).ReturnsAsync((PoracleConfig?)null!);
        this._directory.Setup(d => d.GetAsync()).ReturnsAsync([channelRow]);
        this._humans.Setup(h => h.GetByIdAsync(Channel)).ReturnsAsync(channelRow);
        this._humans.Setup(h => h.GetByIdAsync(Delegate)).ReturnsAsync(personRow);
        this._humans.Setup(h => h.GetAllAsync()).ReturnsAsync([channelRow, personRow]);
        this._jwt.Setup(j => j.GenerateImpersonationToken(It.IsAny<UserInfo>(), It.IsAny<string>())).Returns("impersonation-jwt");

        this._resolver = new UserRoleResolver(
            this._poracleApi.Object,
            this._humanProxy.Object,
            this._delegates.Object,
            this._directory.Object,
            this._siteSettings.Object,
            Options.Create(new PoracleSettings()),
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<UserRoleResolver>.Instance);
    }

    /// <summary>The grant reaches the resolver from the local table, or from PoracleNG's channel list with the opt-in on.</summary>
    private void Grant(string source)
    {
        var channels = source == "poracle" ? new[] { Channel } : [];
        var roles = JsonSerializer.Serialize(new
        {
            admin = new
            {
                discord = new { channels, webhooks = Array.Empty<string>(), users = false },
                telegram = new { channels = Array.Empty<string>(), users = false },
            },
            status = "ok",
        });
        this._humanProxy.Setup(p => p.GetAdminRolesAsync(Delegate)).ReturnsAsync(roles);
        this._delegates.Setup(d => d.GetManagedWebhookIdsAsync(Delegate))
            .ReturnsAsync(source == "local" ? [Channel] : []);
        this._siteSettings.Setup(s => s.GetBoolAsync(UserRoleResolver.PoracleChannelsSettingKey)).ReturnsAsync(source == "poracle");
    }

    private AdminController Admin()
    {
        var sut = new AdminController(
            this._humans.Object,
            new MemoryCache(new MemoryCacheOptions()),
            new Mock<IUserPurgeService>().Object,
            this._delegates.Object,
            this._poracleApi.Object,
            new Mock<IPoracleServerProfileService>().Object,
            new Mock<IUpdateCheckService>().Object,
            new ConfigurationBuilder().Build(),
            this._humanProxy.Object,
            Options.Create(new PoracleSettings()),
            this._jwt.Object,
            this._resolver,
            this._directory.Object,
            new Mock<ILogger<AdminController>>().Object);
        SetupUser(sut, userId: Delegate);
        return sut;
    }

    private AuthController Auth()
    {
        var sut = new AuthController(
            this._humans.Object,
            new Mock<IProfileService>().Object,
            this._poracleApi.Object,
            this._humanProxy.Object,
            PairTestSupport.UnlinkedWriter(),
            this._siteSettings.Object,
            this._delegates.Object,
            this._jwt.Object,
            this._resolver,
            new Mock<Pgan.PoracleWebNet.Api.Services.Oidc.IOidcClient>().Object,
            new Mock<Pgan.PoracleWebNet.Api.Services.Oidc.IOidcSessionService>().Object,
            Options.Create(new DiscordSettings()),
            Options.Create(new TelegramSettings()),
            Options.Create(new OidcSettings()),
            Options.Create(new PoracleSettings()),
            new ConfigurationBuilder().Build(),
            new Mock<IAccountLinkService>().Object,
            new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider(),
            new Mock<IHttpClientFactory>().Object,
            new Mock<ILogger<AuthController>>().Object);
        SetupUser(sut, userId: Delegate);
        return sut;
    }

    [Theory]
    [InlineData("local")]
    [InlineData("poracle")]
    public async Task ADelegatedChannelIsTheSameOnEverySurface(string source)
    {
        this.Grant(source);

        // My Destinations lists it.
        var list = Assert.IsType<OkObjectResult>(await this.Admin().GetManagedDestinations());
        var rows = Assert.IsType<IEnumerable<object>>(list.Value, exactMatch: false).ToList();
        var row = Assert.Single(rows);
        Assert.Equal(Channel, row.GetType().GetProperty("Id")?.GetValue(row));

        // Minting the impersonation token lets them in.
        Assert.IsType<OkObjectResult>(await this.Admin().ImpersonateById(new AdminController.ImpersonateRequest(Channel)));

        // /api/auth/me offers the nav item.
        var me = Assert.IsType<UserInfo>(Assert.IsType<OkObjectResult>(await this.Auth().Me()).Value);
        Assert.Contains(Channel, me.ManagedDestinations ?? []);

        // The per-request re-check keeps the session it just minted.
        var session = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("userId", Channel), new Claim("impersonatedBy", Delegate)], "Test"));
        Assert.True(await new ImpersonationAuthority(this._resolver, NullLogger<ImpersonationAuthority>.Instance).StillHoldsAsync(session));
    }

    /// <summary>
    /// The other half: with the opt-in off, PoracleNG's channel list grants nothing, and every surface says
    /// so together rather than one offering a page another refuses.
    /// </summary>
    [Fact]
    public async Task AChannelPoracleListsWithTheSettingOffIsRefusedOnEverySurface()
    {
        this.Grant("poracle");
        this._siteSettings.Setup(s => s.GetBoolAsync(UserRoleResolver.PoracleChannelsSettingKey)).ReturnsAsync(false);

        var list = Assert.IsType<OkObjectResult>(await this.Admin().GetManagedDestinations());
        Assert.Empty(Assert.IsType<IEnumerable<object>>(list.Value, exactMatch: false));

        Assert.IsType<ForbidResult>(await this.Admin().ImpersonateById(new AdminController.ImpersonateRequest(Channel)));

        var me = Assert.IsType<UserInfo>(Assert.IsType<OkObjectResult>(await this.Auth().Me()).Value);
        Assert.DoesNotContain(Channel, me.ManagedDestinations ?? []);

        var session = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("userId", Channel), new Claim("impersonatedBy", Delegate)], "Test"));
        Assert.False(await new ImpersonationAuthority(this._resolver, NullLogger<ImpersonationAuthority>.Instance).StillHoldsAsync(session));
    }
}
