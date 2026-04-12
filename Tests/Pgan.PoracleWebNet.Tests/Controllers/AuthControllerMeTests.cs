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
    private readonly Mock<IJwtService> _jwtService = new();
    private readonly AuthController _sut;

    public AuthControllerMeTests()
    {
        this._jwtService.Setup(j => j.GenerateToken(It.IsAny<UserInfo>()))
            .Returns("refreshed-jwt-token");

        var config = new ConfigurationBuilder().Build();
        this._sut = new AuthController(
            this._humanService.Object,
            new Mock<IPoracleApiProxy>().Object,
            new Mock<IPoracleHumanProxy>().Object,
            new Mock<ISiteSettingService>().Object,
            new Mock<IWebhookDelegateService>().Object,
            this._jwtService.Object,
            Options.Create(new DiscordSettings()),
            Options.Create(new TelegramSettings()),
            Options.Create(new PoracleSettings()),
            config,
            new Mock<ILogger<AuthController>>().Object);
    }

    [Fact]
    public async Task Me_ReturnsRefreshedToken_WhenProfileNoMismatch()
    {
        SetupUser(this._sut, profileNo: 2);
        this._humanService.Setup(s => s.GetByIdAsync("123456789"))
            .ReturnsAsync(new Human { CurrentProfileNo = 1, Enabled = 1 });

        var result = await this._sut.Me();

        var ok = Assert.IsType<OkObjectResult>(result);
        var userInfo = Assert.IsType<UserInfo>(ok.Value);
        Assert.Equal(1, userInfo.ProfileNo);
        Assert.Equal("refreshed-jwt-token", userInfo.Token);
        this._jwtService.Verify(j => j.GenerateToken(It.Is<UserInfo>(u => u.ProfileNo == 1)), Times.Once);
    }

    [Fact]
    public async Task Me_DoesNotIncludeToken_WhenProfileNoMatches()
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
    public async Task Me_UsesDbProfileNo_WhenHumanExists()
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

    [Fact]
    public async Task Me_FallsBackToJwtProfileNo_WhenHumanNotFound()
    {
        SetupUser(this._sut, profileNo: 2);
        this._humanService.Setup(s => s.GetByIdAsync("123456789"))
            .ReturnsAsync((Human?)null);

        var result = await this._sut.Me();

        var ok = Assert.IsType<OkObjectResult>(result);
        var userInfo = Assert.IsType<UserInfo>(ok.Value);
        Assert.Equal(2, userInfo.ProfileNo);
        Assert.Null(userInfo.Token);
        this._jwtService.Verify(j => j.GenerateToken(It.IsAny<UserInfo>()), Times.Never);
    }
}
