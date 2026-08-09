using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Pgan.PoracleWebNet.Api.Configuration;
using Pgan.PoracleWebNet.Api.Controllers;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Tests.Controllers;

/// <summary>
/// Tests for AuthController.Me() — specifically the JWT/DB profile resync behavior.
/// </summary>
public class AuthControllerMeTests : ControllerTestBase
{
    private readonly Mock<IHumanService> _humanService = new();
    private readonly Mock<IProfileService> _profileService = new();
    private readonly Mock<IJwtService> _jwtService = new();
    private readonly AuthController _sut;

    public AuthControllerMeTests()
    {
        this._jwtService.Setup(j => j.GenerateToken(It.IsAny<UserInfo>()))
            .Returns("refreshed-jwt-token");
        this._jwtService.Setup(j => j.GenerateTokenWithReplacedProfile(It.IsAny<ClaimsPrincipal>(), It.IsAny<int>(), It.IsAny<bool?>()))
            .Returns("refreshed-jwt-token");

        var config = new ConfigurationBuilder().Build();
        this._sut = new AuthController(
            this._humanService.Object, this._profileService.Object,
            new Mock<IPoracleApiProxy>().Object,
            new Mock<IPoracleHumanProxy>().Object,
            new Mock<ISiteSettingService>().Object,
            new Mock<IWebhookDelegateService>().Object,
            this._jwtService.Object,
            new Mock<Pgan.PoracleWebNet.Api.Services.IUserRoleResolver>().Object,
            new Mock<Pgan.PoracleWebNet.Api.Services.Oidc.IOidcClient>().Object,
            new Mock<Pgan.PoracleWebNet.Api.Services.Oidc.IOidcSessionService>().Object,
            Options.Create(new DiscordSettings()),
            Options.Create(new TelegramSettings()),
            Options.Create(new OidcSettings()),
            Options.Create(new PoracleSettings()),
            config,
            new Mock<ILogger<AuthController>>().Object);
    }

    /// <summary>
    /// The SPA renders this in the user menu with a "Profile {n}" fallback and the property did not exist,
    /// so the fallback fired every time. See #520.
    /// </summary>
    [Fact]
    public async Task MeCarriesTheActiveProfileName()
    {
        SetupUser(this._sut, profileNo: 2);
        this._humanService.Setup(s => s.GetByIdAsync("123456789"))
            .ReturnsAsync(new Human { CurrentProfileNo = 2, Enabled = 1 });
        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync("123456789", 2))
            .ReturnsAsync(new Profile { ProfileNo = 2, Name = "Work Profile" });

        var result = await this._sut.Me();

        var userInfo = Assert.IsType<UserInfo>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal("Work Profile", userInfo.ProfileName);
    }

    /// <summary>The label is cosmetic; the enabled flag and the profile resync are not.</summary>
    [Fact]
    public async Task MeStillAnswersWhenTheProfileNameCannotBeRead()
    {
        SetupUser(this._sut, profileNo: 2);
        this._humanService.Setup(s => s.GetByIdAsync("123456789"))
            .ReturnsAsync(new Human { CurrentProfileNo = 2, Enabled = 1 });
        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync("123456789", 2))
            .ThrowsAsync(new HttpRequestException("PoracleNG is down"));

        var result = await this._sut.Me();

        var userInfo = Assert.IsType<UserInfo>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Null(userInfo.ProfileName);
    }

    [Fact]
    public async Task MeReturnsRefreshedTokenWhenProfileNoMismatch()
    {
        SetupUser(this._sut, profileNo: 2);
        this._humanService.Setup(s => s.GetByIdAsync("123456789"))
            .ReturnsAsync(new Human { CurrentProfileNo = 1, Enabled = 1 });

        var result = await this._sut.Me();

        var ok = Assert.IsType<OkObjectResult>(result);
        var userInfo = Assert.IsType<UserInfo>(ok.Value);
        Assert.Equal(1, userInfo.ProfileNo);
        Assert.Equal("refreshed-jwt-token", userInfo.Token);
        this._jwtService.Verify(
            // isAdmin is passed explicitly now: this is the one place a long-lived session routinely
            // re-mints its token, so copying the claim let revoked admin rights survive. See #624.
            j => j.GenerateTokenWithReplacedProfile(It.IsAny<ClaimsPrincipal>(), 1, It.IsAny<bool?>()), Times.Once);
    }

    [Fact]
    public async Task MeDoesNotIncludeTokenWhenProfileNoMatches()
    {
        SetupUser(this._sut, profileNo: 1);
        this._humanService.Setup(s => s.GetByIdAsync("123456789"))
            .ReturnsAsync(new Human { CurrentProfileNo = 1, Enabled = 1 });

        var result = await this._sut.Me();

        var ok = Assert.IsType<OkObjectResult>(result);
        var userInfo = Assert.IsType<UserInfo>(ok.Value);
        Assert.Equal(1, userInfo.ProfileNo);
        Assert.Null(userInfo.Token);
        this._jwtService.Verify(j => j.GenerateToken(It.IsAny<UserInfo>()), Times.Never);
    }

    [Fact]
    public async Task MeUsesDbProfileNoWhenHumanExists()
    {
        SetupUser(this._sut, profileNo: 3);
        this._humanService.Setup(s => s.GetByIdAsync("123456789"))
            .ReturnsAsync(new Human { CurrentProfileNo = 5, Enabled = 1 });

        var result = await this._sut.Me();

        var ok = Assert.IsType<OkObjectResult>(result);
        var userInfo = Assert.IsType<UserInfo>(ok.Value);
        Assert.Equal(5, userInfo.ProfileNo);
        Assert.NotNull(userInfo.Token);
    }

    /// <summary>
    /// A missing row used to read as healthy, so a deleted account kept answering 200 with enabled:true
    /// while every other endpoint threw -- and the SPA, which only signs out on 401, left the user in a
    /// fully rendered app where nothing worked. See #545.
    /// </summary>
    [Fact]
    public async Task MeSignsOutAnAccountThatNoLongerExists()
    {
        SetupUser(this._sut, profileNo: 2);
        this._humanService.Setup(s => s.GetByIdAsync("123456789"))
            .ReturnsAsync((Human?)null);

        var result = await this._sut.Me();

        Assert.IsType<UnauthorizedObjectResult>(result);
        this._jwtService.Verify(
            j => j.GenerateTokenWithReplacedProfile(It.IsAny<ClaimsPrincipal>(), It.IsAny<int>(), It.IsAny<bool?>()), Times.Never);
    }

    /// <summary>
    /// The resync used to rebuild the token from UserInfo, which has no impersonatedBy field, so an admin
    /// impersonation session lost the only record of what it was - on exactly the out-of-band profile
    /// changes this branch exists to absorb. See #484.
    /// </summary>
    [Fact]
    public async Task MeResyncPreservesTheImpersonationClaim()
    {
        SetupUser(this._sut, profileNo: 2);
        ((ClaimsIdentity)this._sut.User.Identity!).AddClaim(new Claim("impersonatedBy", "999"));
        this._humanService.Setup(s => s.GetByIdAsync("123456789"))
            .ReturnsAsync(new Human { CurrentProfileNo = 1, Enabled = 1 });

        ClaimsPrincipal? seen = null;
        this._jwtService
            .Setup(j => j.GenerateTokenWithReplacedProfile(It.IsAny<ClaimsPrincipal>(), It.IsAny<int>(), It.IsAny<bool?>()))
            .Callback<ClaimsPrincipal, int, bool?>((p, _, _) => seen = p)
            .Returns("refreshed-jwt-token");

        await this._sut.Me();

        Assert.Equal("999", seen?.FindFirst("impersonatedBy")?.Value);
    }
}
