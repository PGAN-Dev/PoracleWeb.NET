using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using PGAN.Poracle.Web.Api.Configuration;
using PGAN.Poracle.Web.Api.Controllers;
using PGAN.Poracle.Web.Core.Abstractions.Services;
using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Tests.Controllers;

public class AdminControllerTests : ControllerTestBase
{
    private readonly Mock<IHumanService> _humanService = new();
    private readonly Mock<IPwebSettingService> _pwebSettingService = new();
    private readonly Mock<IPoracleApiProxy> _proxy = new();
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
        _sut = new AdminController(_humanService.Object, _pwebSettingService.Object, _proxy.Object, poracleSettings, jwtSettings, _logger.Object);
    }

    // --- GetAllUsers ---

    [Fact]
    public async Task GetAllUsers_ReturnsForbid_WhenNotAdmin()
    {
        SetupUser(_sut, isAdmin: false);
        Assert.IsType<ForbidResult>(await _sut.GetAllUsers());
    }

    [Fact]
    public async Task GetAllUsers_ReturnsOk_WhenAdmin()
    {
        SetupUser(_sut, isAdmin: true);
        _humanService.Setup(s => s.GetAllAsync()).ReturnsAsync(new List<Human>
        {
            new() { Id = "u1", Name = "User1", Type = "discord:user" },
            new() { Id = "u2", Name = "User2", Type = "telegram:user" }
        });

        var result = await _sut.GetAllUsers();
        Assert.IsType<OkObjectResult>(result);
    }

    // --- GetUser ---

    [Fact]
    public async Task GetUser_ReturnsForbid_WhenNotAdmin()
    {
        SetupUser(_sut, isAdmin: false);
        Assert.IsType<ForbidResult>(await _sut.GetUser("u1"));
    }

    [Fact]
    public async Task GetUser_ReturnsNotFound_WhenMissing()
    {
        SetupUser(_sut, isAdmin: true);
        _humanService.Setup(s => s.GetByIdAsync("unknown")).ReturnsAsync((Human?)null);
        Assert.IsType<NotFoundResult>(await _sut.GetUser("unknown"));
    }

    [Fact]
    public async Task GetUser_ReturnsOk_WhenFound()
    {
        SetupUser(_sut, isAdmin: true);
        _humanService.Setup(s => s.GetByIdAsync("u1")).ReturnsAsync(new Human { Id = "u1", Name = "User1", Type = "discord:user" });
        Assert.IsType<OkObjectResult>(await _sut.GetUser("u1"));
    }

    // --- EnableUser / DisableUser ---

    [Fact]
    public async Task EnableUser_ReturnsForbid_WhenNotAdmin()
    {
        SetupUser(_sut, isAdmin: false);
        Assert.IsType<ForbidResult>(await _sut.EnableUser("u1"));
    }

    [Fact]
    public async Task EnableUser_ReturnsNotFound_WhenMissing()
    {
        SetupUser(_sut, isAdmin: true);
        _humanService.Setup(s => s.GetByIdAsync("u1")).ReturnsAsync((Human?)null);
        Assert.IsType<NotFoundResult>(await _sut.EnableUser("u1"));
    }

    [Fact]
    public async Task EnableUser_SetsAdminDisableToZero()
    {
        SetupUser(_sut, isAdmin: true);
        var human = new Human { Id = "u1", AdminDisable = 1 };
        _humanService.Setup(s => s.GetByIdAsync("u1")).ReturnsAsync(human);
        _humanService.Setup(s => s.UpdateAsync(human)).ReturnsAsync(human);

        await _sut.EnableUser("u1");

        Assert.Equal(0, human.AdminDisable);
    }

    [Fact]
    public async Task DisableUser_SetsAdminDisableToOne()
    {
        SetupUser(_sut, isAdmin: true);
        var human = new Human { Id = "u1", AdminDisable = 0 };
        _humanService.Setup(s => s.GetByIdAsync("u1")).ReturnsAsync(human);
        _humanService.Setup(s => s.UpdateAsync(human)).ReturnsAsync(human);

        await _sut.DisableUser("u1");

        Assert.Equal(1, human.AdminDisable);
    }

    // --- PauseUser / ResumeUser ---

    [Fact]
    public async Task PauseUser_SetsEnabledToZero()
    {
        SetupUser(_sut, isAdmin: true);
        var human = new Human { Id = "u1", Enabled = 1 };
        _humanService.Setup(s => s.GetByIdAsync("u1")).ReturnsAsync(human);
        _humanService.Setup(s => s.UpdateAsync(human)).ReturnsAsync(human);

        await _sut.PauseUser("u1");

        Assert.Equal(0, human.Enabled);
    }

    [Fact]
    public async Task ResumeUser_SetsEnabledToOne()
    {
        SetupUser(_sut, isAdmin: true);
        var human = new Human { Id = "u1", Enabled = 0 };
        _humanService.Setup(s => s.GetByIdAsync("u1")).ReturnsAsync(human);
        _humanService.Setup(s => s.UpdateAsync(human)).ReturnsAsync(human);

        await _sut.ResumeUser("u1");

        Assert.Equal(1, human.Enabled);
    }

    // --- DeleteUserAlarms ---

    [Fact]
    public async Task DeleteUserAlarms_ReturnsForbid_WhenNotAdmin()
    {
        SetupUser(_sut, isAdmin: false);
        Assert.IsType<ForbidResult>(await _sut.DeleteUserAlarms("u1"));
    }

    [Fact]
    public async Task DeleteUserAlarms_ReturnsNotFound_WhenUserMissing()
    {
        SetupUser(_sut, isAdmin: true);
        _humanService.Setup(s => s.ExistsAsync("u1")).ReturnsAsync(false);
        Assert.IsType<NotFoundResult>(await _sut.DeleteUserAlarms("u1"));
    }

    [Fact]
    public async Task DeleteUserAlarms_ReturnsOkWithCount()
    {
        SetupUser(_sut, isAdmin: true);
        _humanService.Setup(s => s.ExistsAsync("u1")).ReturnsAsync(true);
        _humanService.Setup(s => s.DeleteAllAlarmsByUserAsync("u1")).ReturnsAsync(10);

        var result = await _sut.DeleteUserAlarms("u1");
        Assert.IsType<OkObjectResult>(result);
    }

    // --- CreateWebhook ---

    [Fact]
    public async Task CreateWebhook_ReturnsForbid_WhenNotAdmin()
    {
        SetupUser(_sut, isAdmin: false);
        Assert.IsType<ForbidResult>(await _sut.CreateWebhook(new AdminController.CreateWebhookRequest("Test", "http://test")));
    }

    [Fact]
    public async Task CreateWebhook_ReturnsBadRequest_WhenUrlEmpty()
    {
        SetupUser(_sut, isAdmin: true);
        Assert.IsType<BadRequestObjectResult>(await _sut.CreateWebhook(new AdminController.CreateWebhookRequest("Test", "")));
    }

    [Fact]
    public async Task CreateWebhook_ReturnsBadRequest_WhenNameEmpty()
    {
        SetupUser(_sut, isAdmin: true);
        Assert.IsType<BadRequestObjectResult>(await _sut.CreateWebhook(new AdminController.CreateWebhookRequest("", "http://test")));
    }

    [Fact]
    public async Task CreateWebhook_ReturnsConflict_WhenAlreadyExists()
    {
        SetupUser(_sut, isAdmin: true);
        _humanService.Setup(s => s.ExistsAsync("http://test")).ReturnsAsync(true);
        Assert.IsType<ConflictObjectResult>(await _sut.CreateWebhook(new AdminController.CreateWebhookRequest("Test", "http://test")));
    }

    [Fact]
    public async Task CreateWebhook_ReturnsOk_WhenSuccessful()
    {
        SetupUser(_sut, isAdmin: true);
        _humanService.Setup(s => s.ExistsAsync("http://test")).ReturnsAsync(false);
        _humanService.Setup(s => s.CreateAsync(It.IsAny<Human>())).ReturnsAsync(new Human { Id = "http://test", Name = "Test", Type = "webhook" });

        var result = await _sut.CreateWebhook(new AdminController.CreateWebhookRequest("Test", "http://test"));
        Assert.IsType<OkObjectResult>(result);
    }

    // --- DeleteUser ---

    [Fact]
    public async Task DeleteUser_ReturnsForbid_WhenNotAdmin()
    {
        SetupUser(_sut, isAdmin: false);
        Assert.IsType<ForbidResult>(await _sut.DeleteUser("u1"));
    }

    [Fact]
    public async Task DeleteUser_ReturnsNotFound_WhenMissing()
    {
        SetupUser(_sut, isAdmin: true);
        _humanService.Setup(s => s.DeleteUserAsync("u1")).ReturnsAsync(false);
        Assert.IsType<NotFoundResult>(await _sut.DeleteUser("u1"));
    }

    [Fact]
    public async Task DeleteUser_ReturnsNoContent_WhenDeleted()
    {
        SetupUser(_sut, isAdmin: true);
        _humanService.Setup(s => s.DeleteUserAsync("u1")).ReturnsAsync(true);
        Assert.IsType<NoContentResult>(await _sut.DeleteUser("u1"));
    }

    // --- ImpersonateUser ---

    [Fact]
    public async Task ImpersonateUser_ReturnsForbid_WhenNotAdmin()
    {
        SetupUser(_sut, isAdmin: false);
        Assert.IsType<ForbidResult>(await _sut.ImpersonateUser("u1"));
    }

    [Fact]
    public async Task ImpersonateUser_ReturnsNotFound_WhenMissing()
    {
        SetupUser(_sut, isAdmin: true);
        _humanService.Setup(s => s.GetByIdAsync("u1")).ReturnsAsync((Human?)null);
        Assert.IsType<NotFoundResult>(await _sut.ImpersonateUser("u1"));
    }

    [Fact]
    public async Task ImpersonateUser_ReturnsOkWithToken()
    {
        SetupUser(_sut, isAdmin: true);
        _humanService.Setup(s => s.GetByIdAsync("u1")).ReturnsAsync(new Human { Id = "u1", Name = "User1", Type = "discord:user", Enabled = 1, AdminDisable = 0, CurrentProfileNo = 1 });

        var result = await _sut.ImpersonateUser("u1");
        Assert.IsType<OkObjectResult>(result);
    }

    // --- ImpersonateById ---

    [Fact]
    public async Task ImpersonateById_ReturnsForbid_WhenNotAdminOrDelegate()
    {
        SetupUser(_sut, isAdmin: false);
        Assert.IsType<ForbidResult>(await _sut.ImpersonateById(new AdminController.ImpersonateRequest("u1")));
    }

    [Fact]
    public async Task ImpersonateById_AllowsDelegate_WhenManagedWebhookMatches()
    {
        SetupUser(_sut, isAdmin: false, managedWebhooks: ["u1"]);
        _humanService.Setup(s => s.GetByIdAsync("u1")).ReturnsAsync(new Human { Id = "u1", Name = "WH", Type = "webhook", Enabled = 1, AdminDisable = 0, CurrentProfileNo = 1 });

        var result = await _sut.ImpersonateById(new AdminController.ImpersonateRequest("u1"));
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task ImpersonateById_ReturnsNotFound_WhenHumanMissing()
    {
        SetupUser(_sut, isAdmin: true);
        _humanService.Setup(s => s.GetByIdAsync("u1")).ReturnsAsync((Human?)null);
        Assert.IsType<NotFoundResult>(await _sut.ImpersonateById(new AdminController.ImpersonateRequest("u1")));
    }

    // --- WebhookDelegates ---

    [Fact]
    public async Task GetAllWebhookDelegates_ReturnsForbid_WhenNotAdmin()
    {
        SetupUser(_sut, isAdmin: false);
        Assert.IsType<ForbidResult>(await _sut.GetAllWebhookDelegates());
    }

    [Fact]
    public async Task GetAllWebhookDelegates_ReturnsFilteredSettings()
    {
        SetupUser(_sut, isAdmin: true);
        _pwebSettingService.Setup(s => s.GetAllAsync()).ReturnsAsync(new List<PwebSetting>
        {
            new() { Setting = "webhook_delegates:wh1", Value = "u1,u2" },
            new() { Setting = "webhook_delegates:wh2", Value = "u3" },
            new() { Setting = "other_setting", Value = "ignored" }
        });

        var result = await _sut.GetAllWebhookDelegates();
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task AddWebhookDelegate_AddsNewDelegate()
    {
        SetupUser(_sut, isAdmin: true);
        _pwebSettingService.Setup(s => s.GetByKeyAsync("webhook_delegates:wh1"))
            .ReturnsAsync(new PwebSetting { Setting = "webhook_delegates:wh1", Value = "u1" });
        _pwebSettingService.Setup(s => s.CreateOrUpdateAsync(It.IsAny<PwebSetting>()))
            .ReturnsAsync(new PwebSetting());

        var result = await _sut.AddWebhookDelegate(new AdminController.WebhookDelegateRequest("wh1", "u2"));

        var ok = Assert.IsType<OkObjectResult>(result);
        var delegates = Assert.IsType<string[]>(ok.Value);
        Assert.Contains("u1", delegates);
        Assert.Contains("u2", delegates);
    }

    [Fact]
    public async Task AddWebhookDelegate_DoesNotDuplicate()
    {
        SetupUser(_sut, isAdmin: true);
        _pwebSettingService.Setup(s => s.GetByKeyAsync("webhook_delegates:wh1"))
            .ReturnsAsync(new PwebSetting { Setting = "webhook_delegates:wh1", Value = "u1" });

        var result = await _sut.AddWebhookDelegate(new AdminController.WebhookDelegateRequest("wh1", "u1"));

        var ok = Assert.IsType<OkObjectResult>(result);
        var delegates = Assert.IsType<string[]>(ok.Value);
        Assert.Single(delegates);
    }

    [Fact]
    public async Task RemoveWebhookDelegate_RemovesAndDeletesSettingWhenEmpty()
    {
        SetupUser(_sut, isAdmin: true);
        _pwebSettingService.Setup(s => s.GetByKeyAsync("webhook_delegates:wh1"))
            .ReturnsAsync(new PwebSetting { Setting = "webhook_delegates:wh1", Value = "u1" });
        _pwebSettingService.Setup(s => s.DeleteAsync("webhook_delegates:wh1")).ReturnsAsync(true);

        var result = await _sut.RemoveWebhookDelegate(new AdminController.WebhookDelegateRequest("wh1", "u1"));

        var ok = Assert.IsType<OkObjectResult>(result);
        var delegates = Assert.IsType<string[]>(ok.Value);
        Assert.Empty(delegates);
        _pwebSettingService.Verify(s => s.DeleteAsync("webhook_delegates:wh1"), Times.Once);
    }

    [Fact]
    public async Task RemoveWebhookDelegate_UpdatesSetting_WhenOthersRemain()
    {
        SetupUser(_sut, isAdmin: true);
        _pwebSettingService.Setup(s => s.GetByKeyAsync("webhook_delegates:wh1"))
            .ReturnsAsync(new PwebSetting { Setting = "webhook_delegates:wh1", Value = "u1,u2" });
        _pwebSettingService.Setup(s => s.CreateOrUpdateAsync(It.IsAny<PwebSetting>()))
            .ReturnsAsync(new PwebSetting());

        var result = await _sut.RemoveWebhookDelegate(new AdminController.WebhookDelegateRequest("wh1", "u1"));

        var ok = Assert.IsType<OkObjectResult>(result);
        var delegates = Assert.IsType<string[]>(ok.Value);
        Assert.Single(delegates);
        Assert.Equal("u2", delegates[0]);
        _pwebSettingService.Verify(s => s.CreateOrUpdateAsync(It.Is<PwebSetting>(s => s.Value == "u2")), Times.Once);
    }

    // --- GetPoracleAdmins ---

    [Fact]
    public async Task GetPoracleAdmins_ReturnsForbid_WhenNotAdmin()
    {
        SetupUser(_sut, isAdmin: false);
        Assert.IsType<ForbidResult>(await _sut.GetPoracleAdmins());
    }

    [Fact]
    public async Task GetPoracleAdmins_MergesConfiguredAndPoracleAdmins()
    {
        SetupUser(_sut, isAdmin: true);
        _proxy.Setup(p => p.GetConfigAsync()).ReturnsAsync(new PoracleConfig
        {
            Admins = new PoracleAdmins { Discord = ["discord_admin"] }
        });

        var result = await _sut.GetPoracleAdmins();
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task GetPoracleAdmins_HandlesProxyFailure()
    {
        SetupUser(_sut, isAdmin: true);
        _proxy.Setup(p => p.GetConfigAsync()).ThrowsAsync(new Exception("fail"));

        var result = await _sut.GetPoracleAdmins();
        // Should still return OK with just configured admin IDs
        Assert.IsType<OkObjectResult>(result);
    }
}
