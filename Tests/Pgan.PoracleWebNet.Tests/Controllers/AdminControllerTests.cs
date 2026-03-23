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
    private readonly Mock<IPwebSettingService> _pwebSettingService = new();
    private readonly Mock<IPoracleApiProxy> _proxy = new();
    private readonly Mock<IPoracleServerService> _poracleServerService = new();
    private readonly Mock<ILogger<AdminController>> _logger = new();
    private readonly AdminController _sut;

    public AdminControllerTests()
    {
        var poracleSettings = Options.Create(new PoracleSettings { AdminIds = "admin1,admin2" });
        var jwtSettings = Options.Create(new JwtSettings
        {
            Secret = "a-very-long-secret-key-for-jwt-testing-at-least-32-bytes",
            Issuer = "test",
            Audience = "test",
            ExpirationMinutes = 60
        });
        this._sut = new AdminController(this._humanService.Object, this._pwebSettingService.Object, this._proxy.Object, this._poracleServerService.Object, poracleSettings, jwtSettings, this._logger.Object);
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
    public async Task EnableUserSetsAdminDisableToZero()
    {
        SetupUser(this._sut, isAdmin: true);
        var human = new Human { Id = "u1", AdminDisable = 1 };
        this._humanService.Setup(s => s.GetByIdAsync("u1")).ReturnsAsync(human);
        this._humanService.Setup(s => s.UpdateAsync(human)).ReturnsAsync(human);

        await this._sut.EnableUser("u1");

        Assert.Equal(0, human.AdminDisable);
    }

    [Fact]
    public async Task DisableUserSetsAdminDisableToOne()
    {
        SetupUser(this._sut, isAdmin: true);
        var human = new Human { Id = "u1", AdminDisable = 0 };
        this._humanService.Setup(s => s.GetByIdAsync("u1")).ReturnsAsync(human);
        this._humanService.Setup(s => s.UpdateAsync(human)).ReturnsAsync(human);

        await this._sut.DisableUser("u1");

        Assert.Equal(1, human.AdminDisable);
    }

    // --- PauseUser / ResumeUser ---

    [Fact]
    public async Task PauseUserSetsEnabledToZero()
    {
        SetupUser(this._sut, isAdmin: true);
        var human = new Human { Id = "u1", Enabled = 1 };
        this._humanService.Setup(s => s.GetByIdAsync("u1")).ReturnsAsync(human);
        this._humanService.Setup(s => s.UpdateAsync(human)).ReturnsAsync(human);

        await this._sut.PauseUser("u1");

        Assert.Equal(0, human.Enabled);
    }

    [Fact]
    public async Task ResumeUserSetsEnabledToOne()
    {
        SetupUser(this._sut, isAdmin: true);
        var human = new Human { Id = "u1", Enabled = 0 };
        this._humanService.Setup(s => s.GetByIdAsync("u1")).ReturnsAsync(human);
        this._humanService.Setup(s => s.UpdateAsync(human)).ReturnsAsync(human);

        await this._sut.ResumeUser("u1");

        Assert.Equal(1, human.Enabled);
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
    public async Task GetAllWebhookDelegatesReturnsFilteredSettings()
    {
        SetupUser(this._sut, isAdmin: true);
        this._pwebSettingService.Setup(s => s.GetAllAsync()).ReturnsAsync(
        [
            new() { Setting = "webhook_delegates:wh1", Value = "u1,u2" },
            new() { Setting = "webhook_delegates:wh2", Value = "u3" },
            new() { Setting = "other_setting", Value = "ignored" }
        ]);

        var result = await this._sut.GetAllWebhookDelegates();
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task AddWebhookDelegateAddsNewDelegate()
    {
        SetupUser(this._sut, isAdmin: true);
        this._pwebSettingService.Setup(s => s.GetByKeyAsync("webhook_delegates:wh1"))
            .ReturnsAsync(new PwebSetting { Setting = "webhook_delegates:wh1", Value = "u1" });
        this._pwebSettingService.Setup(s => s.CreateOrUpdateAsync(It.IsAny<PwebSetting>()))
            .ReturnsAsync(new PwebSetting());

        var result = await this._sut.AddWebhookDelegate(new AdminController.WebhookDelegateRequest("wh1", "u2"));

        var ok = Assert.IsType<OkObjectResult>(result);
        var delegates = Assert.IsType<string[]>(ok.Value);
        Assert.Contains("u1", delegates);
        Assert.Contains("u2", delegates);
    }

    [Fact]
    public async Task AddWebhookDelegateDoesNotDuplicate()
    {
        SetupUser(this._sut, isAdmin: true);
        this._pwebSettingService.Setup(s => s.GetByKeyAsync("webhook_delegates:wh1"))
            .ReturnsAsync(new PwebSetting { Setting = "webhook_delegates:wh1", Value = "u1" });

        var result = await this._sut.AddWebhookDelegate(new AdminController.WebhookDelegateRequest("wh1", "u1"));

        var ok = Assert.IsType<OkObjectResult>(result);
        var delegates = Assert.IsType<string[]>(ok.Value);
        Assert.Single(delegates);
    }

    [Fact]
    public async Task RemoveWebhookDelegateRemovesAndDeletesSettingWhenEmpty()
    {
        SetupUser(this._sut, isAdmin: true);
        this._pwebSettingService.Setup(s => s.GetByKeyAsync("webhook_delegates:wh1"))
            .ReturnsAsync(new PwebSetting { Setting = "webhook_delegates:wh1", Value = "u1" });
        this._pwebSettingService.Setup(s => s.DeleteAsync("webhook_delegates:wh1")).ReturnsAsync(true);

        var result = await this._sut.RemoveWebhookDelegate(new AdminController.WebhookDelegateRequest("wh1", "u1"));

        var ok = Assert.IsType<OkObjectResult>(result);
        var delegates = Assert.IsType<string[]>(ok.Value);
        Assert.Empty(delegates);
        this._pwebSettingService.Verify(s => s.DeleteAsync("webhook_delegates:wh1"), Times.Once);
    }

    [Fact]
    public async Task RemoveWebhookDelegateUpdatesSettingWhenOthersRemain()
    {
        SetupUser(this._sut, isAdmin: true);
        this._pwebSettingService.Setup(s => s.GetByKeyAsync("webhook_delegates:wh1"))
            .ReturnsAsync(new PwebSetting { Setting = "webhook_delegates:wh1", Value = "u1,u2" });
        this._pwebSettingService.Setup(s => s.CreateOrUpdateAsync(It.IsAny<PwebSetting>()))
            .ReturnsAsync(new PwebSetting());

        var result = await this._sut.RemoveWebhookDelegate(new AdminController.WebhookDelegateRequest("wh1", "u1"));

        var ok = Assert.IsType<OkObjectResult>(result);
        var delegates = Assert.IsType<string[]>(ok.Value);
        Assert.Single(delegates);
        Assert.Equal("u2", delegates[0]);
        this._pwebSettingService.Verify(s => s.CreateOrUpdateAsync(It.Is<PwebSetting>(s => s.Value == "u2")), Times.Once);
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
        // Should still return OK with just configured admin IDs
        Assert.IsType<OkObjectResult>(result);
    }

    // --- GetPoracleServers ---

    [Fact]
    public async Task GetPoracleServersReturnsForbidWhenNotAdmin()
    {
        SetupUser(this._sut, isAdmin: false);
        Assert.IsType<ForbidResult>(await this._sut.GetPoracleServers());
    }

    [Fact]
    public async Task GetPoracleServersReturnsOkWithStatuses()
    {
        SetupUser(this._sut, isAdmin: true);
        var statuses = new List<PoracleServerStatus>
        {
            new() { Name = "Server1", Host = "10.0.0.1", Online = true },
            new() { Name = "Server2", Host = "10.0.0.2", Online = false }
        };
        this._poracleServerService.Setup(s => s.GetServersAsync()).ReturnsAsync(statuses);

        var result = await this._sut.GetPoracleServers();
        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(statuses, ok.Value);
    }

    // --- RestartPoracleServer ---

    [Fact]
    public async Task RestartPoracleServerReturnsForbidWhenNotAdmin()
    {
        SetupUser(this._sut, isAdmin: false);
        Assert.IsType<ForbidResult>(await this._sut.RestartPoracleServer("10.0.0.1"));
    }

    [Fact]
    public async Task RestartPoracleServerReturnsOkWithStatus()
    {
        SetupUser(this._sut, isAdmin: true);
        var status = new PoracleServerStatus { Name = "Server1", Host = "10.0.0.1", Online = true, Message = "pm2 restarted" };
        this._poracleServerService.Setup(s => s.RestartServerAsync("10.0.0.1")).ReturnsAsync(status);

        var result = await this._sut.RestartPoracleServer("10.0.0.1");
        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(status, ok.Value);
    }

    [Fact]
    public async Task RestartPoracleServerReturnsNotFoundWhenHostUnknown()
    {
        SetupUser(this._sut, isAdmin: true);
        this._poracleServerService.Setup(s => s.RestartServerAsync("unknown"))
            .ThrowsAsync(new InvalidOperationException("Server with host 'unknown' not found in configuration."));

        var result = await this._sut.RestartPoracleServer("unknown");
        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task RestartPoracleServerReturns500OnUnexpectedError()
    {
        SetupUser(this._sut, isAdmin: true);
        this._poracleServerService.Setup(s => s.RestartServerAsync("10.0.0.1"))
            .ThrowsAsync(new HttpRequestException("unexpected"));

        var result = await this._sut.RestartPoracleServer("10.0.0.1");
        var statusCode = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, statusCode.StatusCode);
    }

    // --- RestartAllPoracleServers ---

    [Fact]
    public async Task RestartAllPoracleServersReturnsForbidWhenNotAdmin()
    {
        SetupUser(this._sut, isAdmin: false);
        Assert.IsType<ForbidResult>(await this._sut.RestartAllPoracleServers());
    }

    [Fact]
    public async Task RestartAllPoracleServersReturnsOkWithStatuses()
    {
        SetupUser(this._sut, isAdmin: true);
        var statuses = new List<PoracleServerStatus>
        {
            new() { Name = "Server1", Host = "10.0.0.1", Online = true },
            new() { Name = "Server2", Host = "10.0.0.2", Online = true }
        };
        this._poracleServerService.Setup(s => s.RestartAllAsync()).ReturnsAsync(statuses);

        var result = await this._sut.RestartAllPoracleServers();
        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(statuses, ok.Value);
    }
}
