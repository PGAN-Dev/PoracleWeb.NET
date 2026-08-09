using Microsoft.Extensions.Caching.Memory;
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
    private readonly Mock<IUserPurgeService> _userPurgeService = new();
    private readonly Mock<IPoracleApiProxy> _proxy = new();
    private readonly Mock<IPoracleHumanProxy> _humanProxy = new();
    private readonly Mock<IWebhookDelegateService> _webhookDelegateService = new();
    private readonly Mock<IJwtService> _jwtService = new();
    private readonly Mock<Pgan.PoracleWebNet.Api.Services.IUserRoleResolver> _roleResolver = new();
    private readonly Mock<ILogger<AdminController>> _logger = new();
    private readonly AdminController _sut;

    public AdminControllerTests()
    {
        var poracleSettings = Options.Create(new PoracleSettings { AdminIds = "admin1,admin2" });
        this._jwtService.Setup(j => j.GenerateImpersonationToken(It.IsAny<UserInfo>(), It.IsAny<string>()))
            .Returns("test-impersonation-jwt");
        this._sut = new AdminController(
            this._humanService.Object,
            new MemoryCache(new MemoryCacheOptions()),
            this._userPurgeService.Object,
            this._webhookDelegateService.Object,
            this._proxy.Object,
            this._humanProxy.Object,
            poracleSettings,
            this._jwtService.Object,
            this._roleResolver.Object,
            this._logger.Object);
    }

    /// <summary>Both ids must name real accounts before a grant is meaningful. See #514.</summary>
    private void GivenWebhookAndUserExist(string webhookId = "wh1", string userId = "u2")
    {
        this._humanService.Setup(s => s.GetByIdAsync(webhookId))
            .ReturnsAsync(new Human { Id = webhookId, Type = "webhook" });
        this._humanService.Setup(s => s.ExistsAsync(userId)).ReturnsAsync(true);
    }

    // --- GetUserAvatars (#395) ---

    [Fact]
    public void GetUserAvatarsReturnsForbidWhenNotAdmin()
    {
        SetupUser(this._sut, isAdmin: false);
        Assert.IsType<ForbidResult>(this._sut.GetUserAvatars(["u1"]));
    }

    [Fact]
    public void GetUserAvatarsReturnsAnEntryForEveryRequestedId()
    {
        SetupUser(this._sut, isAdmin: true);

        var result = Assert.IsType<OkObjectResult>(this._sut.GetUserAvatars(["111", "222"]));
        var avatars = Assert.IsType<Dictionary<string, string>>(result.Value);

        // Unknown IDs still resolve, to Discord's default avatar rather than nothing -- the caller
        // treats a missing key as "not yet loaded" and would keep asking.
        Assert.Equal(2, avatars.Count);
        Assert.All(avatars.Values, url => Assert.False(string.IsNullOrWhiteSpace(url)));
    }

    [Fact]
    public void GetUserAvatarsHandlesEmptyAndNullInput()
    {
        SetupUser(this._sut, isAdmin: true);

        Assert.Empty(Assert.IsType<Dictionary<string, string>>(
            Assert.IsType<OkObjectResult>(this._sut.GetUserAvatars([])).Value));
        Assert.Empty(Assert.IsType<Dictionary<string, string>>(
            Assert.IsType<OkObjectResult>(this._sut.GetUserAvatars(null!)).Value));
    }

    [Fact]
    public void GetUserAvatarsDedupesAndSkipsBlankIds()
    {
        SetupUser(this._sut, isAdmin: true);

        var result = Assert.IsType<OkObjectResult>(this._sut.GetUserAvatars(["111", "111", "  ", ""]));
        var avatars = Assert.IsType<Dictionary<string, string>>(result.Value);

        Assert.Single(avatars);
        Assert.True(avatars.ContainsKey("111"));
    }

    [Fact]
    public void GetUserAvatarsCapsTheBatchSize()
    {
        SetupUser(this._sut, isAdmin: true);

        var ids = Enumerable.Range(1, 500).Select(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        var result = Assert.IsType<OkObjectResult>(this._sut.GetUserAvatars(ids));
        var avatars = Assert.IsType<Dictionary<string, string>>(result.Value);

        Assert.Equal(200, avatars.Count);
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

    [Fact]
    public async Task DisableUserRefusesToBlockTheCallersOwnAccount()
    {
        // A block is enforced on every request, so this would take the admin's own API access away --
        // including the endpoint that would give it back. See #613.
        SetupUser(this._sut, userId: "u1", isAdmin: true);

        var result = await this._sut.DisableUser("u1");

        Assert.IsType<BadRequestObjectResult>(result);
        this._humanProxy.Verify(p => p.AdminDisabledAsync(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task DisableUserStillBlocksOtherAccounts()
    {
        SetupUser(this._sut, userId: "admin", isAdmin: true);
        this._humanService.Setup(s => s.GetByIdAsync("u1")).ReturnsAsync(new Human { Id = "u1" });

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
        // The delete goes through the purge service now, so everything the account owns goes with it.
        // See #510, #511, #512.
        this._userPurgeService.Setup(s => s.PurgeAsync("u1")).ReturnsAsync(false);
        Assert.IsType<NotFoundResult>(await this._sut.DeleteUser("u1"));
    }

    [Fact]
    public async Task DeleteUserReturnsNoContentWhenDeleted()
    {
        SetupUser(this._sut, isAdmin: true);
        this._userPurgeService.Setup(s => s.PurgeAsync("u1")).ReturnsAsync(true);

        Assert.IsType<NoContentResult>(await this._sut.DeleteUser("u1"));

        this._userPurgeService.Verify(s => s.PurgeAsync("u1"), Times.Once);
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
        // Delegation is resolved live now, not read from the JWT claim, so a revoked delegate loses access
        // immediately rather than at their next sign-in. See #601.
        // Resolved through IUserRoleResolver, which unions the local delegate table with the webhooks
        // PoracleNG reports -- a PoracleJS-configured delegate was refused by the local-table-only
        // lookup. See #626.
        this._roleResolver.Setup(r => r.ResolveAsync("123456789"))
            .ReturnsAsync(new Pgan.PoracleWebNet.Api.Services.UserRoles(false, ["u1"]));
        this._humanService.Setup(s => s.GetByIdAsync("u1")).ReturnsAsync(new Human { Id = "u1", Name = "WH", Type = "webhook", Enabled = 1, AdminDisable = 0, CurrentProfileNo = 1 });

        var result = await this._sut.ImpersonateById(new AdminController.ImpersonateRequest("u1"));
        Assert.IsType<OkObjectResult>(result);
    }

    /// <summary>
    /// The JWT claim is minted at login and lives 24 hours, so trusting it let a revoked delegate keep
    /// impersonating the webhook until they next signed in. See #601.
    /// </summary>
    [Fact]
    public async Task ImpersonateByIdRefusesADelegateWhoseGrantWasRevoked()
    {
        SetupUser(this._sut, isAdmin: false, managedWebhooks: ["u1"]);
        this._webhookDelegateService.Setup(s => s.GetManagedWebhookIdsAsync("123456789"))
            .ReturnsAsync([]);

        Assert.IsType<ForbidResult>(
            await this._sut.ImpersonateById(new AdminController.ImpersonateRequest("u1")));
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

    /// <summary>
    /// webhookId was length-checked and userId was not, though its column is half the width: over 100
    /// characters surfaced as an unhandled DbUpdateException, and an empty string persisted a delegate
    /// granting nothing to nobody that then appeared in the admin view. See #483.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AddWebhookDelegateRejectsAnEmptyUserId(string userId)
    {
        SetupUser(this._sut, isAdmin: true);

        var result = await this._sut.AddWebhookDelegate(new AdminController.WebhookDelegateRequest("wh1", userId));

        Assert.IsType<BadRequestObjectResult>(result);
        this._webhookDelegateService.Verify(
            s => s.AddDelegateAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task AddWebhookDelegateRejectsAUserIdLongerThanItsColumn()
    {
        SetupUser(this._sut, isAdmin: true);

        var result = await this._sut.AddWebhookDelegate(
            new AdminController.WebhookDelegateRequest("wh1", new string('9', 101)));

        Assert.IsType<BadRequestObjectResult>(result);
        this._webhookDelegateService.Verify(
            s => s.AddDelegateAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task AddWebhookDelegateAcceptsAUserIdExactlyAtTheLimit()
    {
        SetupUser(this._sut, isAdmin: true);
        var userId = new string('9', 100);
        this.GivenWebhookAndUserExist(userId: userId);
        this._webhookDelegateService.Setup(s => s.AddDelegateAsync("wh1", userId))
            .ReturnsAsync([userId]);

        var result = await this._sut.AddWebhookDelegate(new AdminController.WebhookDelegateRequest("wh1", userId));

        Assert.IsType<OkObjectResult>(result);
    }
    [Fact]
    public async Task AddWebhookDelegateRejectsAWebhookThatDoesNotExist()
    {
        SetupUser(this._sut, isAdmin: true);
        this._humanService.Setup(s => s.GetByIdAsync("wh-ghost")).ReturnsAsync((Human?)null);

        var result = await this._sut.AddWebhookDelegate(new AdminController.WebhookDelegateRequest("wh-ghost", "u2"));

        Assert.IsType<BadRequestObjectResult>(result);
        this._webhookDelegateService.Verify(
            s => s.AddDelegateAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    /// <summary>A grant may only name a webhook, not an ordinary user account.</summary>
    [Fact]
    public async Task AddWebhookDelegateRejectsAnIdThatIsNotAWebhook()
    {
        SetupUser(this._sut, isAdmin: true);
        this._humanService.Setup(s => s.GetByIdAsync("someone"))
            .ReturnsAsync(new Human { Id = "someone", Type = "discord:user" });

        var result = await this._sut.AddWebhookDelegate(new AdminController.WebhookDelegateRequest("someone", "u2"));

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task AddWebhookDelegateRejectsAUserThatDoesNotExist()
    {
        SetupUser(this._sut, isAdmin: true);
        this._humanService.Setup(s => s.GetByIdAsync("wh1"))
            .ReturnsAsync(new Human { Id = "wh1", Type = "webhook" });
        this._humanService.Setup(s => s.ExistsAsync("ghost")).ReturnsAsync(false);

        var result = await this._sut.AddWebhookDelegate(new AdminController.WebhookDelegateRequest("wh1", "ghost"));

        Assert.IsType<BadRequestObjectResult>(result);
        this._webhookDelegateService.Verify(
            s => s.AddDelegateAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task AddWebhookDelegateAddsNewDelegate()
    {
        SetupUser(this._sut, isAdmin: true);
        this.GivenWebhookAndUserExist();
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
