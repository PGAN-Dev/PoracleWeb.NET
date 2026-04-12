using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Pgan.PoracleWebNet.Api.Configuration;
using Pgan.PoracleWebNet.Api.Controllers;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Tests.Controllers;

public class AdminControllerTests : ControllerTestBase
{
    private readonly Mock<IHumanService> _humanService = new();
    private readonly Mock<IPoracleApiProxy> _proxy = new();
    private readonly Mock<IPoracleHumanProxy> _humanProxy = new();
    private readonly Mock<IWebhookDelegateService> _webhookDelegateService = new();
    private readonly Mock<IJwtService> _jwtService = new();
    private readonly Mock<ILogger<AdminController>> _logger = new();
    private readonly AdminController _sut;

    public AdminControllerTests()
    {
        var poracleSettings = Options.Create(new PoracleSettings { AdminIds = "admin1,admin2" });
        this._jwtService.Setup(j => j.GenerateImpersonationToken(It.IsAny<UserInfo>(), It.IsAny<string>()))
            .Returns("test-impersonation-jwt");
        this._sut = new AdminController(
            this._humanService.Object,
            this._webhookDelegateService.Object,
            this._proxy.Object,
            this._humanProxy.Object,
            poracleSettings,
            this._jwtService.Object,
            this._logger.Object);
    }

    // --- GetAllUsers ---

    [Fact]
    public async Task GetAllUsersReturnsForbidWhenNotAdmin()
    {
        SetupUser(this._sut, isAdmin: false);
        Assert.IsType<ForbidResult>(await this._sut.GetAllUsers());
    }

    [Fact]
    public async Task GetAllUsersReturnsOkWhenAdmin()
    {
        SetupUser(this._sut, isAdmin: true);
        this._humanService.Setup(s => s.GetAllAsync()).ReturnsAsync(
        [
            new() { Id = "u1", Name = "User1", Type = "discord:user" },
            new() { Id = "u2", Name = "User2", Type = "telegram:user" }
        ]);

        var result = await this._sut.GetAllUsers();
        Assert.IsType<OkObjectResult>(result);
    }

    // --- GetUser ---

    [Fact]
    public async Task GetUserReturnsForbidWhenNotAdmin()
    {
        SetupUser(this._sut, isAdmin: false);
        Assert.IsType<ForbidResult>(await this._sut.GetUser("u1"));
    }

    [Fact]
    public async Task GetUserReturnsNotFoundWhenMissing()
    {
        SetupUser(this._sut, isAdmin: true);
        this._humanService.Setup(s => s.GetByIdAsync("unknown")).ReturnsAsync((Human?)null);
        Assert.IsType<NotFoundResult>(await this._sut.GetUser("unknown"));
    }

    [Fact]
    public async Task GetUserReturnsOkWhenFound()
    {
        SetupUser(this._sut, isAdmin: true);
        this._humanService.Setup(s => s.GetByIdAsync("u1")).ReturnsAsync(new Human { Id = "u1", Name = "User1", Type = "discord:user" });
        Assert.IsType<OkObjectResult>(await this._sut.GetUser("u1"));
    }

    // --- EnableUser / DisableUser ---

    [Fact]
    public async Task EnableUserReturnsForbidWhenNotAdmin()
    {
        SetupUser(this._sut, isAdmin: false);
        Assert.IsType<ForbidResult>(await this._sut.EnableUser("u1"));
    }

    [Fact]
    public async Task EnableUserReturnsNotFoundWhenMissing()
    {
        SetupUser(this._sut, isAdmin: true);
        this._humanService.Setup(s => s.GetByIdAsync("u1")).ReturnsAsync((Human?)null);
        Assert.IsType<NotFoundResult>(await this._sut.EnableUser("u1"));
    }

    [Fact]
    public async Task EnableUserCallsProxyAdminDisabledFalse()
    {
        SetupUser(this._sut, isAdmin: true);
        var human = new Human { Id = "u1", AdminDisable = 1 };
        this._humanService.Setup(s => s.GetByIdAsync("u1")).ReturnsAsync(human);

        await this._sut.EnableUser("u1");

        this._humanProxy.Verify(p => p.AdminDisabledAsync("u1", false), Times.Once);
    }

    [Fact]
    public async Task DisableUserCallsProxyAdminDisabledTrue()
    {
        SetupUser(this._sut, isAdmin: true);
        var human = new Human { Id = "u1", AdminDisable = 0 };
        this._humanService.Setup(s => s.GetByIdAsync("u1")).ReturnsAsync(human);

        await this._sut.DisableUser("u1");

        this._humanProxy.Verify(p => p.AdminDisabledAsync("u1", true), Times.Once);
    }

    // --- PauseUser / ResumeUser ---

    [Fact]
    public async Task PauseUserCallsProxyStop()
    {
        SetupUser(this._sut, isAdmin: true);
        var human = new Human { Id = "u1", Enabled = 1 };
        this._humanService.Setup(s => s.GetByIdAsync("u1")).ReturnsAsync(human);

        await this._sut.PauseUser("u1");

        this._humanProxy.Verify(p => p.StopAsync("u1"), Times.Once);
    }

    [Fact]
    public async Task ResumeUserCallsProxyStart()
    {
        SetupUser(this._sut, isAdmin: true);
        var human = new Human { Id = "u1", Enabled = 0 };
        this._humanService.Setup(s => s.GetByIdAsync("u1")).ReturnsAsync(human);

        await this._sut.ResumeUser("u1");

        this._humanProxy.Verify(p => p.StartAsync("u1"), Times.Once);
    }

    // --- DeleteUserAlarms ---

    [Fact]
    public async Task DeleteUserAlarmsReturnsForbidWhenNotAdmin()
    {
        SetupUser(this._sut, isAdmin: false);
        Assert.IsType<ForbidResult>(await this._sut.DeleteUserAlarms("u1"));
    }

    [Fact]
    public async Task DeleteUserAlarmsReturnsNotFoundWhenUserMissing()
    {
        SetupUser(this._sut, isAdmin: true);
        this._humanService.Setup(s => s.ExistsAsync("u1")).ReturnsAsync(false);
        Assert.IsType<NotFoundResult>(await this._sut.DeleteUserAlarms("u1"));
    }

    [Fact]
    public async Task DeleteUserAlarmsReturnsOkWithCount()
    {
        SetupUser(this._sut, isAdmin: true);
        this._humanService.Setup(s => s.ExistsAsync("u1")).ReturnsAsync(true);
        this._humanService.Setup(s => s.DeleteAllAlarmsByUserAsync("u1")).ReturnsAsync(10);

        var result = await this._sut.DeleteUserAlarms("u1");
        Assert.IsType<OkObjectResult>(result);
    }

    // --- CreateWebhook ---

    [Fact]
    public async Task CreateWebhookReturnsForbidWhenNotAdmin()
    {
        SetupUser(this._sut, isAdmin: false);
        Assert.IsType<ForbidResult>(await this._sut.CreateWebhook(new AdminController.CreateWebhookRequest("Test", "http://test")));
    }

    [Fact]
    public async Task CreateWebhookReturnsBadRequestWhenUrlEmpty()
    {
        SetupUser(this._sut, isAdmin: true);
        Assert.IsType<BadRequestObjectResult>(await this._sut.CreateWebhook(new AdminController.CreateWebhookRequest("Test", "")));
    }

    [Fact]
    public async Task CreateWebhookReturnsBadRequestWhenNameEmpty()
    {
        SetupUser(this._sut, isAdmin: true);
        Assert.IsType<BadRequestObjectResult>(await this._sut.CreateWebhook(new AdminController.CreateWebhookRequest("", "http://test")));
    }

    [Fact]
    public async Task CreateWebhookReturnsConflictWhenAlreadyExists()
    {
        SetupUser(this._sut, isAdmin: true);
        this._humanService.Setup(s => s.ExistsAsync("http://test")).ReturnsAsync(true);
        Assert.IsType<ConflictObjectResult>(await this._sut.CreateWebhook(new AdminController.CreateWebhookRequest("Test", "http://test")));
    }

    [Fact]
    public async Task CreateWebhookReturnsOkWhenSuccessful()
    {
        SetupUser(this._sut, isAdmin: true);
        this._humanService.Setup(s => s.ExistsAsync("http://test")).ReturnsAsync(false);
        this._humanService.Setup(s => s.CreateAsync(It.IsAny<Human>())).ReturnsAsync(new Human { Id = "http://test", Name = "Test", Type = "webhook" });

        var result = await this._sut.CreateWebhook(new AdminController.CreateWebhookRequest("Test", "http://test"));
        Assert.IsType<OkObjectResult>(result);
    }

    // --- DeleteUser ---

    [Fact]
    public async Task DeleteUserReturnsForbidWhenNotAdmin()
    {
        SetupUser(this._sut, isAdmin: false);
        Assert.IsType<ForbidResult>(await this._sut.DeleteUser("u1"));
    }

    [Fact]
    public async Task DeleteUserReturnsNotFoundWhenMissing()
    {
        SetupUser(this._sut, isAdmin: true);
        this._humanService.Setup(s => s.DeleteUserAsync("u1")).ReturnsAsync(false);
        Assert.IsType<NotFoundResult>(await this._sut.DeleteUser("u1"));
    }

    [Fact]
    public async Task DeleteUserReturnsNoContentWhenDeleted()
    {
        SetupUser(this._sut, isAdmin: true);
        this._humanService.Setup(s => s.DeleteUserAsync("u1")).ReturnsAsync(true);
        Assert.IsType<NoContentResult>(await this._sut.DeleteUser("u1"));
    }

    // --- ImpersonateUser ---

    [Fact]
    public async Task ImpersonateUserReturnsForbidWhenNotAdmin()
    {
        SetupUser(this._sut, isAdmin: false);
        Assert.IsType<ForbidResult>(await this._sut.ImpersonateUser("u1"));
    }

    [Fact]
    public async Task ImpersonateUserReturnsNotFoundWhenMissing()
    {
        SetupUser(this._sut, isAdmin: true);
        this._humanService.Setup(s => s.GetByIdAsync("u1")).ReturnsAsync((Human?)null);
        Assert.IsType<NotFoundResult>(await this._sut.ImpersonateUser("u1"));
    }

    [Fact]
    public async Task ImpersonateUserReturnsOkWithToken()
    {
        SetupUser(this._sut, isAdmin: true);
        this._humanService.Setup(s => s.GetByIdAsync("u1")).ReturnsAsync(new Human { Id = "u1", Name = "User1", Type = "discord:user", Enabled = 1, AdminDisable = 0, CurrentProfileNo = 1 });

        var result = await this._sut.ImpersonateUser("u1");
        Assert.IsType<OkObjectResult>(result);
    }

    // --- ImpersonateById ---

    [Fact]
    public async Task ImpersonateByIdReturnsForbidWhenNotAdminOrDelegate()
    {
        SetupUser(this._sut, isAdmin: false);
        Assert.IsType<ForbidResult>(await this._sut.ImpersonateById(new AdminController.ImpersonateRequest("u1")));
    }

    [Fact]
    public async Task ImpersonateByIdAllowsDelegateWhenManagedWebhookMatches()
    {
        SetupUser(this._sut, isAdmin: false, managedWebhooks: ["u1"]);
        this._humanService.Setup(s => s.GetByIdAsync("u1")).ReturnsAsync(new Human { Id = "u1", Name = "WH", Type = "webhook", Enabled = 1, AdminDisable = 0, CurrentProfileNo = 1 });

        var result = await this._sut.ImpersonateById(new AdminController.ImpersonateRequest("u1"));
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task ImpersonateByIdReturnsNotFoundWhenHumanMissing()
    {
        SetupUser(this._sut, isAdmin: true);
        this._humanService.Setup(s => s.GetByIdAsync("u1")).ReturnsAsync((Human?)null);
        Assert.IsType<NotFoundResult>(await this._sut.ImpersonateById(new AdminController.ImpersonateRequest("u1")));
    }

    // --- WebhookDelegates ---

    [Fact]
    public async Task GetAllWebhookDelegatesReturnsForbidWhenNotAdmin()
    {
        SetupUser(this._sut, isAdmin: false);
        Assert.IsType<ForbidResult>(await this._sut.GetAllWebhookDelegates());
    }

    [Fact]
    public async Task GetAllWebhookDelegatesReturnsGroupedDelegates()
    {
        SetupUser(this._sut, isAdmin: true);
        this._webhookDelegateService.Setup(s => s.GetAllGroupedAsync()).ReturnsAsync(
            new Dictionary<string, string[]>
            {
                ["wh1"] = ["u1", "u2"],
                ["wh2"] = ["u3"]
            });

        var result = await this._sut.GetAllWebhookDelegates();
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task AddWebhookDelegateAddsNewDelegate()
    {
        SetupUser(this._sut, isAdmin: true);
        this._webhookDelegateService.Setup(s => s.AddDelegateAsync("wh1", "u2"))
            .ReturnsAsync(["u1", "u2"]);

        var result = await this._sut.AddWebhookDelegate(new AdminController.WebhookDelegateRequest("wh1", "u2"));

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task RemoveWebhookDelegateRemovesDelegate()
    {
        SetupUser(this._sut, isAdmin: true);
        this._webhookDelegateService.Setup(s => s.RemoveDelegateAsync("wh1", "u1"))
            .ReturnsAsync([]);

        var result = await this._sut.RemoveWebhookDelegate(new AdminController.WebhookDelegateRequest("wh1", "u1"));

        Assert.IsType<OkObjectResult>(result);
        this._webhookDelegateService.Verify(s => s.RemoveDelegateAsync("wh1", "u1"), Times.Once);
    }

    // --- GetPoracleAdmins ---

    [Fact]
    public async Task GetPoracleAdminsReturnsForbidWhenNotAdmin()
    {
        SetupUser(this._sut, isAdmin: false);
        Assert.IsType<ForbidResult>(await this._sut.GetPoracleAdmins());
    }

    [Fact]
    public async Task GetPoracleAdminsMergesConfiguredAndPoracleAdmins()
    {
        SetupUser(this._sut, isAdmin: true);
        this._proxy.Setup(p => p.GetConfigAsync()).ReturnsAsync(new PoracleConfig
        {
            Admins = new PoracleAdmins { Discord = ["discord_admin"] }
        });

        var result = await this._sut.GetPoracleAdmins();
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task GetPoracleAdminsHandlesProxyFailure()
    {
        SetupUser(this._sut, isAdmin: true);
        this._proxy.Setup(p => p.GetConfigAsync()).ThrowsAsync(new InvalidOperationException("fail"));

        var result = await this._sut.GetPoracleAdmins();
        Assert.IsType<OkObjectResult>(result);
    }
}
