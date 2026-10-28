using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Pgan.PoracleWebNet.Api.Configuration;
using Pgan.PoracleWebNet.Api.Controllers;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Tests.Helpers;

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
    private readonly Mock<Pgan.PoracleWebNet.Api.Services.IDestinationDirectory> _directory = new();
    private readonly Mock<IPoracleServerProfileService> _serverProfile = new();
    private readonly Mock<IUpdateCheckService> _updateCheck = new();
    private readonly Mock<ILogger<AdminController>> _logger = new();
    private readonly AdminController _sut;

    public AdminControllerTests()
    {
        this._jwtService.Setup(j => j.GenerateImpersonationToken(It.IsAny<UserInfo>(), It.IsAny<string>()))
            .Returns("test-impersonation-jwt");
        this._sut = this.CreateSut(this._logger.Object);
    }

    /// <summary>The controller over the shared mocks, logging to <paramref name="logger"/>.</summary>
    private AdminController CreateSut(ILogger<AdminController> logger) => new(
        this._humanService.Object,
        new MemoryCache(new MemoryCacheOptions()),
        this._userPurgeService.Object,
        this._webhookDelegateService.Object,
        this._proxy.Object,
        this._serverProfile.Object,
        this._updateCheck.Object,
        new ConfigurationBuilder().Build(),
        this._humanProxy.Object,
        Options.Create(new PoracleSettings { AdminIds = "admin1,admin2" }),
        this._jwtService.Object,
        this._roleResolver.Object,
        this._directory.Object,
        logger);

    /// <summary>Both ids must name real accounts before a grant is meaningful. See #514.</summary>
    private void GivenWebhookAndUserExist(string webhookId = "wh1", string userId = "u2")
    {
        this._humanService.Setup(s => s.GetByIdAsync(webhookId))
            .ReturnsAsync(new Human { Id = webhookId, Type = "webhook" });
        this._humanService.Setup(s => s.GetByIdAsync(userId))
            .ReturnsAsync(new Human { Id = userId, Type = "discord:user" });
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

    [Fact]
    public async Task GetAllUsersStampsEachRowsKind()
    {
        SetupUser(this._sut, isAdmin: true);
        this._humanService.Setup(s => s.GetAllAsync()).ReturnsAsync(
        [
            new() { Id = "a", Type = "discord:user" },
            new() { Id = "b", Type = "webhook" },
            new() { Id = "c", Type = "telegram:group" },
            new() { Id = "d", Type = "x:unknown" },
            new() { Id = "e", Type = null }
        ]);

        var result = await this._sut.GetAllUsers();

        var rows = Assert.IsAssignableFrom<IEnumerable<object>>(Assert.IsType<OkObjectResult>(result).Value);
        var kinds = rows.Select(r => (string?)r.GetType().GetProperty("Kind")?.GetValue(r)).ToArray();
        Assert.Equal(["person", "destination", "destination", "destination", "person"], kinds);
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

    /// <summary>
    /// The half-write cleanup asks PoracleNG whether the webhook exists. A failed read used to answer
    /// "no" and now throws (#994); thrown from inside the catch it replaced the 502 with a bare 500. The
    /// admin still gets the 502 -- and is not told nothing was created, since that could not be checked.
    /// </summary>
    [Fact]
    public async Task CreateWebhookStillAnswers502WhenTheCleanupCannotReachPoracle()
    {
        SetupUser(this._sut, isAdmin: true);
        this._humanService.Setup(s => s.ExistsAsync("http://test")).ReturnsAsync(false);
        this._humanService.Setup(s => s.GetByIdAsync("http://test"))
            .ThrowsAsync(new HttpRequestException("503", null, System.Net.HttpStatusCode.ServiceUnavailable));
        this._humanService.Setup(s => s.CreateAsync(It.IsAny<Human>())).ThrowsAsync(new HttpRequestException("boom"));

        var result = await this._sut.CreateWebhook(new AdminController.CreateWebhookRequest("Test", "http://test"));

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(Microsoft.AspNetCore.Http.StatusCodes.Status502BadGateway, objectResult.StatusCode);
        Assert.DoesNotContain(
            "Nothing was created",
            objectResult.Value?.GetType().GetProperty("error")?.GetValue(objectResult.Value) as string ?? string.Empty,
            StringComparison.Ordinal);
        this._humanService.Verify(s => s.DeleteUserAsync(It.IsAny<string>()), Times.Never);
        this._userPurgeService.Verify(p => p.PurgeAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CreateWebhookStillUndoesAHalfWriteItCanSee()
    {
        // The legitimate case beside the one above: Poracle answers, the row is there, so it goes. See #482.
        SetupUser(this._sut, isAdmin: true);
        this._humanService.Setup(s => s.ExistsAsync("http://test")).ReturnsAsync(false);
        this._humanService.Setup(s => s.GetByIdAsync("http://test"))
            .ReturnsAsync(new Human { Id = "http://test", Name = "Test", Type = "webhook" });
        this._userPurgeService.Setup(p => p.PurgeAsync("http://test")).ReturnsAsync(true);
        this._humanService.Setup(s => s.CreateAsync(It.IsAny<Human>())).ThrowsAsync(new HttpRequestException("boom"));

        var result = await this._sut.CreateWebhook(new AdminController.CreateWebhookRequest("Test", "http://test"));

        Assert.Equal(Microsoft.AspNetCore.Http.StatusCodes.Status502BadGateway, Assert.IsType<ObjectResult>(result).StatusCode);
        this._userPurgeService.Verify(p => p.PurgeAsync("http://test"), Times.Once);
    }

    /// <summary>
    /// A create that fails while another request wrote the same id must leave that row alone. The rollback
    /// used to delete whatever it found at the id (#1028).
    /// </summary>
    [Fact]
    public async Task CreateWebhookLeavesARowAnotherRequestWrote()
    {
        SetupUser(this._sut, isAdmin: true);
        this._humanService.SetupSequence(s => s.ExistsAsync("http://test")).ReturnsAsync(false).ReturnsAsync(true);
        this._humanService.Setup(s => s.GetByIdAsync("http://test"))
            .ReturnsAsync(new Human { Id = "http://test", Name = "Someone else", Type = "webhook" });
        this._humanService.Setup(s => s.CreateAsync(It.IsAny<Human>())).ThrowsAsync(new HttpRequestException("boom"));

        var result = await this._sut.CreateWebhook(new AdminController.CreateWebhookRequest("Test", "http://test"));

        Assert.Equal(Microsoft.AspNetCore.Http.StatusCodes.Status502BadGateway, Assert.IsType<ObjectResult>(result).StatusCode);
        this._userPurgeService.Verify(p => p.PurgeAsync(It.IsAny<string>()), Times.Never);
        this._humanService.Verify(s => s.DeleteUserAsync(It.IsAny<string>()), Times.Never);
    }

    // --- CreateDestination (#1025) ---

    private static string? ErrorOf(object? value) =>
        value?.GetType().GetProperty("error")?.GetValue(value) as string;

    /// <summary>The six kinds that grant power, read from the one list that defines them.</summary>
    public static TheoryData<string> EveryDestinationKind
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var kind in HumanKind.Destinations)
            {
                data.Add(kind);
            }

            return data;
        }
    }

    /// <summary>
    /// The directory is cached a minute. A create that fails may still have written the row (PoracleNG
    /// commits the human first), and a rollback removes one, so every failure branch has to drop the cache:
    /// otherwise the grant picker offers a row that is gone, or hides one that is there.
    /// </summary>
    [Theory]
    [InlineData("discord:channel", "raids", false)] // our half-write, rolled back
    [InlineData("discord:channel", "someone else's", false)] // another request's row, left alone
    [InlineData(null, null, false)] // nothing written
    [InlineData(null, null, true)] // could not be asked
    public async Task AFailedCreateDropsTheCachedDirectoryOnEveryBranch(string? foundType, string? foundName, bool readFails)
    {
        SetupUser(this._sut, isAdmin: true);
        this._humanService.Setup(s => s.ExistsAsync("123456789012345678")).ReturnsAsync(false);
        if (readFails)
        {
            this._humanService.Setup(s => s.GetByIdAsync("123456789012345678"))
                .ThrowsAsync(new HttpRequestException("503", null, System.Net.HttpStatusCode.ServiceUnavailable));
        }
        else
        {
            this._humanService.Setup(s => s.GetByIdAsync("123456789012345678")).ReturnsAsync(
                foundType is null ? null : new Human { Id = "123456789012345678", Name = foundName, Type = foundType });
        }

        this._humanService.Setup(s => s.CreateAsync(It.IsAny<Human>())).ThrowsAsync(new HttpRequestException("boom"));
        this._userPurgeService.Setup(p => p.PurgeAsync("123456789012345678")).ReturnsAsync(true);

        var result = await this._sut.CreateDestination(
            new AdminController.CreateDestinationRequest("discord:channel", "123456789012345678", "raids", null));

        Assert.Equal(Microsoft.AspNetCore.Http.StatusCodes.Status502BadGateway, Assert.IsType<ObjectResult>(result).StatusCode);
        this._directory.Verify(d => d.Invalidate(), Times.AtLeastOnce);
    }

    /// <summary>
    /// A grant PoracleNG reports by name for an id that did not exist yet was dropped by the resolver, and that
    /// answer is cached; a new destination has to drop it too, or the grant stays invisible for a minute.
    /// </summary>
    [Fact]
    public async Task CreatingADestinationRefreshesRoleAnswers()
    {
        SetupUser(this._sut, isAdmin: true);
        this._humanService.Setup(s => s.ExistsAsync("123456789012345678")).ReturnsAsync(false);
        this._humanService.Setup(s => s.CreateAsync(It.IsAny<Human>())).ReturnsAsync((Human h) => h);

        var result = await this._sut.CreateDestination(
            new AdminController.CreateDestinationRequest("discord:channel", "123456789012345678", "raids", null));

        Assert.IsType<OkObjectResult>(result);
        this._roleResolver.Verify(r => r.InvalidateAll(), Times.Once);
    }

    /// <summary>The directory is cached a minute; a new destination has to be grantable straight away.</summary>
    [Fact]
    public async Task CreatingADestinationRefreshesTheDirectory()
    {
        SetupUser(this._sut, isAdmin: true);
        this._humanService.Setup(s => s.ExistsAsync("123456789012345678")).ReturnsAsync(false);
        this._humanService.Setup(s => s.CreateAsync(It.IsAny<Human>())).ReturnsAsync((Human h) => h);

        var result = await this._sut.CreateDestination(
            new AdminController.CreateDestinationRequest("discord:channel", "123456789012345678", "raids", null));

        Assert.IsType<OkObjectResult>(result);
        this._directory.Verify(d => d.Invalidate(), Times.Once);
    }

    [Fact]
    public async Task CreateDestinationCreatesADiscordChannel()
    {
        SetupUser(this._sut, isAdmin: true);
        this._humanService.Setup(s => s.ExistsAsync("123456789012345678")).ReturnsAsync(false);
        Human? sent = null;
        this._humanService.Setup(s => s.CreateAsync(It.IsAny<Human>()))
            .Callback<Human>(h => sent = h)
            .ReturnsAsync((Human h) => h);

        var result = await this._sut.CreateDestination(
            new AdminController.CreateDestinationRequest("discord:channel", " 123456789012345678 ", " raids ", "Avoca raids"));

        Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(sent);
        Assert.Equal("discord:channel", sent!.Type);
        Assert.Equal("123456789012345678", sent.Id);
        Assert.Equal("raids", sent.Name);
        Assert.Equal("Avoca raids", sent.Notes);
        Assert.Equal(1, sent.Enabled);
        Assert.Equal(0, sent.AdminDisable);
    }

    /// <summary>A supergroup id (<c>-100…</c>) and a legacy basic-group id must both be accepted.</summary>
    [Theory]
    [InlineData("telegram:group", "-1001234567890")]
    [InlineData("telegram:group", "-1234567890")]
    [InlineData("telegram:channel", "-1001234567890")]
    [InlineData("telegram:topic", "-1001234567890:42")]
    [InlineData("discord:thread", "123456789012345678")]
    public async Task CreateDestinationAcceptsEveryKindsRealId(string type, string id)
    {
        SetupUser(this._sut, isAdmin: true);
        this._humanService.Setup(s => s.ExistsAsync(id)).ReturnsAsync(false);
        this._humanService.Setup(s => s.CreateAsync(It.IsAny<Human>())).ReturnsAsync((Human h) => h);

        var result = await this._sut.CreateDestination(new AdminController.CreateDestinationRequest(type, id, "Name", null));

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task CreateDestinationRefusesAPersonType()
    {
        SetupUser(this._sut, isAdmin: true);

        var result = await this._sut.CreateDestination(
            new AdminController.CreateDestinationRequest("discord:user", "123456789012345678", "Someone", null));

        Assert.IsType<BadRequestObjectResult>(result);
        this._humanService.Verify(s => s.CreateAsync(It.IsAny<Human>()), Times.Never);
    }

    [Fact]
    public async Task CreateDestinationRefusesAnUnknownType()
    {
        SetupUser(this._sut, isAdmin: true);

        var result = await this._sut.CreateDestination(
            new AdminController.CreateDestinationRequest("x:unknown", "123456789012345678", "Typo", null));

        Assert.IsType<BadRequestObjectResult>(result);
        this._humanService.Verify(s => s.CreateAsync(It.IsAny<Human>()), Times.Never);
    }

    [Theory]
    [InlineData("telegram:group", "1234", "Group")]
    [InlineData("discord:channel", "123456789012345678", " ")]
    [InlineData("discord:channel", "123456789012345678", "raids\u0007")]
    [InlineData("discord:channel", " ", "raids")]
    public async Task CreateDestinationRefusesAnImpossibleIdOrName(string type, string id, string name)
    {
        SetupUser(this._sut, isAdmin: true);

        var result = await this._sut.CreateDestination(new AdminController.CreateDestinationRequest(type, id, name, null));

        Assert.IsType<BadRequestObjectResult>(result);
        this._humanService.Verify(s => s.CreateAsync(It.IsAny<Human>()), Times.Never);
    }

    [Fact]
    public async Task CreateDestinationRefusesANonAdmin()
    {
        SetupUser(this._sut, isAdmin: false);

        Assert.IsType<ForbidResult>(await this._sut.CreateDestination(
            new AdminController.CreateDestinationRequest("discord:channel", "123456789012345678", "raids", null)));
    }

    [Fact]
    public async Task CreateDestinationRefusesWhileImpersonating()
    {
        SetupImpersonatingUser(this._sut, isAdmin: true);

        Assert.IsType<ForbidResult>(await this._sut.CreateDestination(
            new AdminController.CreateDestinationRequest("discord:channel", "123456789012345678", "raids", null)));
        this._humanService.Verify(s => s.CreateAsync(It.IsAny<Human>()), Times.Never);
    }

    [Fact]
    public async Task CreateDestinationNamesTheRowAlreadyThere()
    {
        SetupUser(this._sut, isAdmin: true);
        this._humanService.Setup(s => s.ExistsAsync("123456789012345678")).ReturnsAsync(true);
        this._humanService.Setup(s => s.GetByIdAsync("123456789012345678"))
            .ReturnsAsync(new Human { Id = "123456789012345678", Name = "old-raids", Type = "discord:thread" });

        var result = await this._sut.CreateDestination(
            new AdminController.CreateDestinationRequest("discord:channel", "123456789012345678", "raids", null));

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        var body = conflict.Value!;
        Assert.False(string.IsNullOrWhiteSpace(ErrorOf(body)));
        Assert.Equal("discord:thread", body.GetType().GetProperty("type")?.GetValue(body));
        Assert.Equal("old-raids", body.GetType().GetProperty("name")?.GetValue(body));
        this._humanService.Verify(s => s.CreateAsync(It.IsAny<Human>()), Times.Never);
    }

    /// <summary>
    /// The detail read is a courtesy. A timeout there (TaskCanceledException, not HttpRequestException) still
    /// answers the 409, just without naming the row, rather than a 500.
    /// </summary>
    [Fact]
    public async Task CreateDestinationStillAnswers409WhenTheDetailReadTimesOut()
    {
        SetupUser(this._sut, isAdmin: true);
        this._humanService.Setup(s => s.ExistsAsync("123456789012345678")).ReturnsAsync(true);
        this._humanService.Setup(s => s.GetByIdAsync("123456789012345678"))
            .ThrowsAsync(new TaskCanceledException("timed out"));

        var result = await this._sut.CreateDestination(
            new AdminController.CreateDestinationRequest("discord:channel", "123456789012345678", "raids", null));

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        var body = conflict.Value!;
        Assert.False(string.IsNullOrWhiteSpace(ErrorOf(body)));
        Assert.Null(body.GetType().GetProperty("type")?.GetValue(body));
        Assert.Null(body.GetType().GetProperty("name")?.GetValue(body));
        this._humanService.Verify(s => s.CreateAsync(It.IsAny<Human>()), Times.Never);
    }

    /// <summary>
    /// The #1028 shape through this endpoint: a failed create undoes only a row with this request's type
    /// and name, never one another request wrote at the same id.
    /// </summary>
    [Theory]
    [InlineData("discord:channel", "raids", true)]
    [InlineData("discord:channel", "someone else's", false)]
    [InlineData("discord:thread", "raids", false)]
    public async Task CreateDestinationRollsBackOnlyItsOwnRow(string foundType, string foundName, bool expectPurge)
    {
        SetupUser(this._sut, isAdmin: true);
        this._humanService.Setup(s => s.ExistsAsync("123456789012345678")).ReturnsAsync(false);
        this._humanService.Setup(s => s.GetByIdAsync("123456789012345678"))
            .ReturnsAsync(new Human { Id = "123456789012345678", Name = foundName, Type = foundType });
        this._humanService.Setup(s => s.CreateAsync(It.IsAny<Human>())).ThrowsAsync(new HttpRequestException("boom"));
        this._userPurgeService.Setup(p => p.PurgeAsync("123456789012345678")).ReturnsAsync(true);

        var result = await this._sut.CreateDestination(
            new AdminController.CreateDestinationRequest("discord:channel", "123456789012345678", "raids", null));

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(Microsoft.AspNetCore.Http.StatusCodes.Status502BadGateway, objectResult.StatusCode);
        Assert.DoesNotContain("webhook", ErrorOf(objectResult.Value) ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        this._userPurgeService.Verify(p => p.PurgeAsync("123456789012345678"), expectPurge ? Times.Once() : Times.Never());
    }

    /// <summary>
    /// <c>POST /webhooks</c> now goes through the destination path, which refuses an id that is not a URL.
    /// The legitimate case: an http URL with a port, the shape CLAUDE.md gives for webhook ids.
    /// </summary>
    [Theory]
    [InlineData("not a url", false)]
    [InlineData("http://host:8080/path", true)]
    public async Task CreateWebhookChecksTheUrl(string url, bool expectOk)
    {
        SetupUser(this._sut, isAdmin: true);
        this._humanService.Setup(s => s.ExistsAsync(url)).ReturnsAsync(false);
        this._humanService.Setup(s => s.CreateAsync(It.IsAny<Human>())).ReturnsAsync((Human h) => h);

        var result = await this._sut.CreateWebhook(new AdminController.CreateWebhookRequest("Test", url));

        if (expectOk)
        {
            Assert.IsType<OkObjectResult>(result);
        }
        else
        {
            Assert.IsType<BadRequestObjectResult>(result);
        }
    }

    /// <summary>
    /// Enable, disable, pause and resume re-read the account after the write so the admin sees what was
    /// stored. The write has already landed by then; a failed re-read (which throws since #994) must not
    /// turn it into a 500. Falling back to the record read before the write is what they always did.
    /// </summary>
    [Theory]
    [InlineData("enable")]
    [InlineData("disable")]
    [InlineData("pause")]
    [InlineData("resume")]
    public async Task AUserStateChangeThatLandedIsNotReportedAsAFailureWhenTheReReadFails(string action)
    {
        SetupUser(this._sut, userId: "admin", isAdmin: true);
        var before = new Human { Id = "u1" };
        this._humanService.SetupSequence(s => s.GetByIdAsync("u1"))
            .ReturnsAsync(before)
            .ThrowsAsync(new HttpRequestException("503", null, System.Net.HttpStatusCode.ServiceUnavailable));

        var result = action switch
        {
            "enable" => await this._sut.EnableUser("u1"),
            "disable" => await this._sut.DisableUser("u1"),
            "pause" => await this._sut.PauseUser("u1"),
            _ => await this._sut.ResumeUser("u1"),
        };

        Assert.Same(before, Assert.IsType<OkObjectResult>(result).Value);
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

    [Fact]
    public async Task DeleteUserRefusesToDeleteTheCallersOwnAccount()
    {
        // The same shape as #613's self-block: the purge takes the admin's own account, alarms and
        // grants with it, and nothing on the site can give them back.
        SetupUser(this._sut, userId: "u1", isAdmin: true);

        var result = await this._sut.DeleteUser("u1");

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("own account", System.Text.Json.JsonSerializer.Serialize(bad.Value), StringComparison.Ordinal);
        this._userPurgeService.Verify(s => s.PurgeAsync(It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [InlineData("admin2")]
    [InlineData("u2")]
    public async Task DeleteUserStillDeletesOtherAdminsAndUsers(string target)
    {
        SetupUser(this._sut, userId: "admin1", isAdmin: true);
        this._userPurgeService.Setup(s => s.PurgeAsync(target)).ReturnsAsync(true);

        Assert.IsType<NoContentResult>(await this._sut.DeleteUser(target));

        this._userPurgeService.Verify(s => s.PurgeAsync(target), Times.Once);
    }

    /// <summary>
    /// The purge removes grants in both directions, and a delegate named in PoracleJS by the webhook's
    /// NAME appears in no table this site can enumerate -- so every cached role answer is dropped, not a
    /// list of the ones we happen to know about.
    /// </summary>
    [Fact]
    public async Task DeleteUserDropsEveryCachedRoleAnswer()
    {
        SetupUser(this._sut, isAdmin: true);
        this._userPurgeService.Setup(s => s.PurgeAsync("u1")).ReturnsAsync(true);

        await this._sut.DeleteUser("u1");

        this._roleResolver.Verify(r => r.InvalidateAll(), Times.Once);
    }

    /// <summary>
    /// The destination directory is cached a minute. Deleting, blocking or unblocking an account drops it,
    /// so a grant check never answers from a list that still holds a deleted destination.
    /// </summary>
    [Fact]
    public async Task DeleteUserRefreshesTheDirectory()
    {
        SetupUser(this._sut, isAdmin: true);
        this._userPurgeService.Setup(s => s.PurgeAsync("u1")).ReturnsAsync(true);

        await this._sut.DeleteUser("u1");

        this._directory.Verify(d => d.Invalidate(), Times.Once);
    }

    [Fact]
    public async Task BlockAndUnblockRefreshTheDirectory()
    {
        SetupUser(this._sut, userId: "admin", isAdmin: true);
        this._humanService.Setup(s => s.GetByIdAsync("u1")).ReturnsAsync(new Human { Id = "u1" });

        await this._sut.DisableUser("u1");
        await this._sut.EnableUser("u1");

        this._directory.Verify(d => d.Invalidate(), Times.Exactly(2));
    }

    [Fact]
    public async Task AddWebhookDelegateTakesEffectWithoutWaitingOutTheRoleCache()
    {
        SetupUser(this._sut, isAdmin: true);
        this.GivenWebhookAndUserExist();
        this._webhookDelegateService.Setup(s => s.AddDelegateAsync("wh1", "u2")).ReturnsAsync(["u2"]);

        await this._sut.AddWebhookDelegate(new AdminController.WebhookDelegateRequest("wh1", "u2"));

        this._roleResolver.Verify(
            r => r.Invalidate(It.Is<string[]>(ids => ids.Contains("u2") && ids.Contains("wh1"))), Times.Once);
    }

    [Fact]
    public async Task RemoveWebhookDelegateTakesEffectWithoutWaitingOutTheRoleCache()
    {
        SetupUser(this._sut, isAdmin: true);
        this._webhookDelegateService.Setup(s => s.RemoveDelegateAsync("wh1", "u2")).ReturnsAsync([]);

        await this._sut.RemoveWebhookDelegate(new AdminController.WebhookDelegateRequest("wh1", "u2"));

        this._roleResolver.Verify(
            r => r.Invalidate(It.Is<string[]>(ids => ids.Contains("u2") && ids.Contains("wh1"))), Times.Once);
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
        // The claim still names u1; the live answer no longer does. Arranged explicitly: an unset mock
        // answers default(UserRoles), which would pass this test whatever the controller read.
        SetupUser(this._sut, isAdmin: false, managedWebhooks: ["u1"]);
        this._roleResolver.Setup(r => r.ResolveAsync("123456789"))
            .ReturnsAsync(new Pgan.PoracleWebNet.Api.Services.UserRoles(false, null));
        this._humanService.Setup(s => s.GetByIdAsync("u1"))
            .ReturnsAsync(new Human { Id = "u1", Name = "WH", Type = "webhook", Enabled = 1, AdminDisable = 0, CurrentProfileNo = 1 });

        Assert.IsType<ForbidResult>(
            await this._sut.ImpersonateById(new AdminController.ImpersonateRequest("u1")));
        this._jwtService.Verify(j => j.GenerateImpersonationToken(It.IsAny<UserInfo>(), It.IsAny<string>()), Times.Never);
    }

    /// <summary>
    /// A grant can only ever reach a destination. If the row behind a granted id is a person -- a local
    /// grant written before the type changed, or a PoracleNG list that names a user -- a delegate would
    /// otherwise sign in as that person. The type is read from the live row, not from the grant. See #1025.
    /// </summary>
    [Theory]
    [InlineData("discord:user")]
    [InlineData("telegram:user")]
    [InlineData(null)]
    [InlineData("")]
    public async Task ImpersonateByIdRefusesADelegateWhoseTargetIsAPerson(string? type)
    {
        SetupUser(this._sut, isAdmin: false);
        this._roleResolver.Setup(r => r.ResolveAsync("123456789"))
            .ReturnsAsync(new Pgan.PoracleWebNet.Api.Services.UserRoles(false, ["555"]));
        this._humanService.Setup(s => s.GetByIdAsync("555"))
            .ReturnsAsync(new Human { Id = "555", Name = "Someone", Type = type, Enabled = 1, CurrentProfileNo = 1 });

        Assert.IsType<ForbidResult>(await this._sut.ImpersonateById(new AdminController.ImpersonateRequest("555")));
        this._jwtService.Verify(j => j.GenerateImpersonationToken(It.IsAny<UserInfo>(), It.IsAny<string>()), Times.Never);
    }

    /// <summary>Review Focus 1: an unknown type is listed on Destinations but grants no power, this one included.</summary>
    [Fact]
    public async Task ImpersonateByIdRefusesADelegateWhoseTargetHasAnUnknownType()
    {
        SetupUser(this._sut, isAdmin: false);
        this._roleResolver.Setup(r => r.ResolveAsync("123456789"))
            .ReturnsAsync(new Pgan.PoracleWebNet.Api.Services.UserRoles(false, ["typo"]));
        this._humanService.Setup(s => s.GetByIdAsync("typo"))
            .ReturnsAsync(new Human { Id = "typo", Name = "Typo", Type = "x:unknown", Enabled = 1, CurrentProfileNo = 1 });

        Assert.IsType<ForbidResult>(await this._sut.ImpersonateById(new AdminController.ImpersonateRequest("typo")));
    }

    [Theory]
    [InlineData("discord:channel")]
    [InlineData("DISCORD:CHANNEL")]
    [InlineData("discord:thread")]
    [InlineData("telegram:channel")]
    [InlineData("telegram:group")]
    [InlineData("telegram:topic")]
    [InlineData("webhook")]
    public async Task ImpersonateByIdLetsADelegateManageAChannel(string type)
    {
        SetupUser(this._sut, isAdmin: false);
        this._roleResolver.Setup(r => r.ResolveAsync("123456789"))
            .ReturnsAsync(new Pgan.PoracleWebNet.Api.Services.UserRoles(false, ["1100000000000000001"]));
        this._humanService.Setup(s => s.GetByIdAsync("1100000000000000001"))
            .ReturnsAsync(new Human { Id = "1100000000000000001", Name = "raids", Type = type, Enabled = 1, CurrentProfileNo = 1 });

        Assert.IsType<OkObjectResult>(
            await this._sut.ImpersonateById(new AdminController.ImpersonateRequest("1100000000000000001")));
        this._jwtService.Verify(
            j => j.GenerateImpersonationToken(It.Is<UserInfo>(u => u.Id == "1100000000000000001"), "123456789"), Times.Once);
    }

    /// <summary>
    /// The legitimate case beside the refusals: the type check is for delegates. An admin inspects people
    /// through this endpoint too, and any type at all.
    /// </summary>
    [Theory]
    [InlineData("discord:user")]
    [InlineData("x:unknown")]
    public async Task ImpersonateByIdStillLetsAnAdminInspectAnyAccount(string type)
    {
        SetupUser(this._sut, isAdmin: true);
        this._humanService.Setup(s => s.GetByIdAsync("555"))
            .ReturnsAsync(new Human { Id = "555", Name = "Someone", Type = type, Enabled = 1, CurrentProfileNo = 1 });

        Assert.IsType<OkObjectResult>(await this._sut.ImpersonateById(new AdminController.ImpersonateRequest("555")));
    }

    [Fact]
    public async Task ImpersonateByIdReturnsNotFoundWhenHumanMissing()
    {
        SetupUser(this._sut, isAdmin: true);
        this._humanService.Setup(s => s.GetByIdAsync("u1")).ReturnsAsync((Human?)null);
        Assert.IsType<NotFoundResult>(await this._sut.ImpersonateById(new AdminController.ImpersonateRequest("u1")));
    }

    /// <summary>
    /// No second hop from inside an impersonation session. The SPA stashes the caller's own token in a
    /// single poracle_admin_token slot, so a second hop overwrites it with the first impersonated one and
    /// strands whoever started with no way back. Reachable only since #797 put the My Webhooks nav item
    /// back inside impersonation sessions; nothing legitimate chained before it, and nothing does now.
    /// </summary>
    [Fact]
    public async Task ImpersonateByIdRefusesAHopFromInsideAnImpersonationSession()
    {
        SetupImpersonatingUser(this._sut, isAdmin: true);
        this._roleResolver.Setup(r => r.ResolveAsync(It.IsAny<string>()))
            .ReturnsAsync(new Pgan.PoracleWebNet.Api.Services.UserRoles(false, ["u1"]));
        this._humanService.Setup(s => s.GetByIdAsync("u1"))
            .ReturnsAsync(new Human { Id = "u1", Name = "WH", Type = "webhook", Enabled = 1, AdminDisable = 0, CurrentProfileNo = 1 });

        Assert.IsType<ForbidResult>(await this._sut.ImpersonateById(new AdminController.ImpersonateRequest("u1")));
    }

    /// <summary>
    /// The page moved to /my-destinations. /my-webhooks stays as an alias so a SPA still cached from
    /// before the rename keeps loading its rows until the next reload.
    /// </summary>
    [Fact]
    public void GetManagedDestinationsAnswersOnBothRoutes()
    {
        var templates = typeof(AdminController).GetMethod(nameof(AdminController.GetManagedDestinations))!
            .GetCustomAttributes(typeof(HttpGetAttribute), false)
            .Cast<HttpGetAttribute>()
            .Select(a => a.Template)
            .ToArray();

        Assert.Contains("my-destinations", templates);
        Assert.Contains("my-webhooks", templates);
    }

    // --- GetManagedDestinations (#797) ---

    /// <summary>
    /// The grant PoracleNG reports is the webhook's NAME, and this page matches on humans.id. The
    /// resolver canonicalises it, so what arrives here is an id and the row is found -- the delegate
    /// used to get the nav item and an empty table. See #797.
    /// </summary>
    [Fact]
    public async Task GetManagedDestinationsListsTheWebhookADelegateWasGrantedByName()
    {
        SetupUser(this._sut, isAdmin: false);
        this._roleResolver.Setup(r => r.ResolveAsync("123456789"))
            .ReturnsAsync(new Pgan.PoracleWebNet.Api.Services.UserRoles(false, ["http://wh.example/a"]));
        this._humanService.Setup(s => s.GetAllAsync()).ReturnsAsync(
        [
            new Human { Id = "http://wh.example/a", Name = "teamharmonyrares", Type = "webhook" },
            new Human { Id = "http://wh.example/b", Name = "100iv", Type = "webhook" },
        ]);

        var ok = Assert.IsType<OkObjectResult>(await this._sut.GetManagedDestinations());

        var rows = Assert.IsAssignableFrom<IEnumerable<object>>(ok.Value);
        Assert.Single(rows);
    }

    /// <summary>
    /// A degraded resolve can hand back raw grant strings. A row whose type is not one of the six destination
    /// kinds would be listed here and then refused by impersonate, so it is not listed.
    /// </summary>
    [Fact]
    public async Task GetManagedDestinationsListsOnlyKnownDestinationKinds()
    {
        SetupUser(this._sut, isAdmin: false);
        this._roleResolver.Setup(r => r.ResolveAsync("123456789"))
            .ReturnsAsync(new Pgan.PoracleWebNet.Api.Services.UserRoles(false, ["http://wh.example/a", "42", "777"]));
        this._humanService.Setup(s => s.GetAllAsync()).ReturnsAsync(
        [
            new Human { Id = "http://wh.example/a", Name = "rares", Type = "webhook" },
            new Human { Id = "42", Name = "Typo row", Type = "x:unknown" },
            new Human { Id = "777", Name = "Sam", Type = "discord:user" },
        ]);

        var ok = Assert.IsType<OkObjectResult>(await this._sut.GetManagedDestinations());

        var row = Assert.Single(Assert.IsAssignableFrom<IEnumerable<object>>(ok.Value));
        Assert.Equal("http://wh.example/a", row.GetType().GetProperty("Id")?.GetValue(row));
    }

    [Fact]
    public async Task GetManagedDestinationsReturnsNothingWhenTheCallerHasNoGrants()
    {
        SetupUser(this._sut, isAdmin: false);
        this._roleResolver.Setup(r => r.ResolveAsync("123456789"))
            .ReturnsAsync(new Pgan.PoracleWebNet.Api.Services.UserRoles(false, null));

        var ok = Assert.IsType<OkObjectResult>(await this._sut.GetManagedDestinations());

        Assert.Empty(Assert.IsAssignableFrom<IEnumerable<object>>(ok.Value));
        this._humanService.Verify(s => s.GetAllAsync(), Times.Never);
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

    /// <summary>A grant names a destination. A person is not one, so nobody can be delegated another person.</summary>
    [Fact]
    public async Task AddWebhookDelegateRefusesAPersonAsTarget()
    {
        SetupUser(this._sut, isAdmin: true);
        this._humanService.Setup(s => s.GetByIdAsync("someone"))
            .ReturnsAsync(new Human { Id = "someone", Type = "discord:user" });

        var result = await this._sut.AddWebhookDelegate(new AdminController.WebhookDelegateRequest("someone", "u2"));

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("webhookId does not name a destination.", ErrorOf(bad.Value));
        this._webhookDelegateService.Verify(
            s => s.AddDelegateAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    /// <summary>Every kind in HumanKind.Destinations takes delegates now, not only webhooks. See #1025.</summary>
    [Theory]
    [MemberData(nameof(EveryDestinationKind))]
    public async Task AddWebhookDelegateGrantsEveryDestinationKind(string type)
    {
        SetupUser(this._sut, isAdmin: true);
        this.GivenWebhookAndUserExist(webhookId: "123456789012345678");
        this._humanService.Setup(s => s.GetByIdAsync("123456789012345678"))
            .ReturnsAsync(new Human { Id = "123456789012345678", Type = type });
        this._webhookDelegateService.Setup(s => s.AddDelegateAsync("123456789012345678", "u2")).ReturnsAsync(["u2"]);

        var result = await this._sut.AddWebhookDelegate(new AdminController.WebhookDelegateRequest("123456789012345678", "u2"));

        Assert.IsType<OkObjectResult>(result);
        this._webhookDelegateService.Verify(s => s.AddDelegateAsync("123456789012345678", "u2"), Times.Once);
    }

    /// <summary>
    /// A type that isn't a person but isn't a known destination either (an operator's typo) is listed on
    /// Destinations, but it grants nothing: only HumanKind.Destinations does.
    /// </summary>
    [Fact]
    public async Task AddWebhookDelegateRefusesAnUnknownType()
    {
        SetupUser(this._sut, isAdmin: true);
        this.GivenWebhookAndUserExist(webhookId: "typo");
        this._humanService.Setup(s => s.GetByIdAsync("typo"))
            .ReturnsAsync(new Human { Id = "typo", Type = "x:unknown" });

        var result = await this._sut.AddWebhookDelegate(new AdminController.WebhookDelegateRequest("typo", "u2"));

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("webhookId does not name a destination.", ErrorOf(bad.Value));
        this._webhookDelegateService.Verify(
            s => s.AddDelegateAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task AddWebhookDelegateRejectsAUserThatDoesNotExist()
    {
        SetupUser(this._sut, isAdmin: true);
        this._humanService.Setup(s => s.GetByIdAsync("wh1"))
            .ReturnsAsync(new Human { Id = "wh1", Type = "webhook" });
        this._humanService.Setup(s => s.GetByIdAsync("ghost")).ReturnsAsync((Human?)null);

        var result = await this._sut.AddWebhookDelegate(new AdminController.WebhookDelegateRequest("wh1", "ghost"));

        Assert.IsType<BadRequestObjectResult>(result);
        this._webhookDelegateService.Verify(
            s => s.AddDelegateAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task AddWebhookDelegateRefusesAChannelAsGrantee()
    {
        SetupUser(this._sut, isAdmin: true);
        this.GivenWebhookAndUserExist();
        this._humanService.Setup(s => s.GetByIdAsync("u2"))
            .ReturnsAsync(new Human { Id = "u2", Type = "discord:channel" });

        var result = await this._sut.AddWebhookDelegate(new AdminController.WebhookDelegateRequest("wh1", "u2"));

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("userId does not name a person.", bad.Value?.GetType().GetProperty("error")?.GetValue(bad.Value));
        this._webhookDelegateService.Verify(
            s => s.AddDelegateAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task AddWebhookDelegateStillGrantsADiscordUser()
    {
        SetupUser(this._sut, isAdmin: true);
        this.GivenWebhookAndUserExist();
        this._webhookDelegateService.Setup(s => s.AddDelegateAsync("wh1", "u2")).ReturnsAsync(["u2"]);

        var result = await this._sut.AddWebhookDelegate(new AdminController.WebhookDelegateRequest("wh1", "u2"));

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task AddWebhookDelegateAcceptsAGranteeWithNoTypeAsAPerson()
    {
        SetupUser(this._sut, isAdmin: true);
        this.GivenWebhookAndUserExist();
        this._humanService.Setup(s => s.GetByIdAsync("u2"))
            .ReturnsAsync(new Human { Id = "u2", Type = null });
        this._webhookDelegateService.Setup(s => s.AddDelegateAsync("wh1", "u2")).ReturnsAsync(["u2"]);

        var result = await this._sut.AddWebhookDelegate(new AdminController.WebhookDelegateRequest("wh1", "u2"));

        Assert.IsType<OkObjectResult>(result);
        this._webhookDelegateService.Verify(s => s.AddDelegateAsync("wh1", "u2"), Times.Once);
    }

    /// <summary>
    /// The directory the resolver canonicalises grants against is cached a minute, so a destination created
    /// outside this site in that minute would be granted here and still not resolve. Granting drops it.
    /// </summary>
    [Fact]
    public async Task AddWebhookDelegateRefreshesTheDirectory()
    {
        SetupUser(this._sut, isAdmin: true);
        this.GivenWebhookAndUserExist();
        this._webhookDelegateService.Setup(s => s.AddDelegateAsync("wh1", "u2")).ReturnsAsync(["u2"]);

        Assert.IsType<OkObjectResult>(
            await this._sut.AddWebhookDelegate(new AdminController.WebhookDelegateRequest("wh1", "u2")));

        this._directory.Verify(d => d.Invalidate(), Times.Once);
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

    // --- Logged ids (#1038) ---

    private const string WebhookToken = "to_ken-VALUE";
    private const string WebhookUrl = "https://discord.com/api/webhooks/863186716952100874/" + WebhookToken;
    private const string MaskedWebhook = "https://discord.com/api/webhooks/863186716952100874/***";

    /// <summary>
    /// A webhook's id is its URL, and a Discord webhook URL carries the token that lets anyone post to the
    /// channel. Creating one logged the whole URL.
    /// </summary>
    [Fact]
    public async Task CreatingAWebhookLogsItWithoutItsToken()
    {
        var log = new CapturingLogger<AdminController>();
        var sut = this.CreateSut(log);
        SetupUser(sut, isAdmin: true);
        this._humanService.Setup(s => s.ExistsAsync(WebhookUrl)).ReturnsAsync(false);
        this._humanService.Setup(s => s.CreateAsync(It.IsAny<Human>()))
            .ReturnsAsync(new Human { Id = WebhookUrl, Name = "Raids", Type = "webhook" });

        Assert.IsType<OkObjectResult>(await sut.CreateWebhook(new AdminController.CreateWebhookRequest("Raids", WebhookUrl)));

        Assert.Contains(MaskedWebhook, log.All, StringComparison.Ordinal);
        Assert.DoesNotContain(WebhookToken, log.All, StringComparison.Ordinal);
    }

    /// <summary>The legitimate case beside it: a channel id is logged as it is, so the line still names the channel.</summary>
    [Fact]
    public async Task CreatingAChannelStillLogsItsId()
    {
        var log = new CapturingLogger<AdminController>();
        var sut = this.CreateSut(log);
        SetupUser(sut, userId: "admin-1", isAdmin: true);
        this._humanService.Setup(s => s.ExistsAsync("123456789012345678")).ReturnsAsync(false);
        this._humanService.Setup(s => s.CreateAsync(It.IsAny<Human>()))
            .ReturnsAsync(new Human { Id = "123456789012345678", Name = "raids", Type = "discord:channel" });

        Assert.IsType<OkObjectResult>(await sut.CreateDestination(
            new AdminController.CreateDestinationRequest("discord:channel", "123456789012345678", "raids", null)));

        Assert.Contains("Admin admin-1 created discord:channel destination 123456789012345678", log.All, StringComparison.Ordinal);
    }

    /// <summary>A delegate manages a webhook by impersonating it, and the log line named the target in full.</summary>
    [Fact]
    public async Task ADelegateImpersonatingAWebhookIsLoggedWithoutItsToken()
    {
        var log = new CapturingLogger<AdminController>();
        var sut = this.CreateSut(log);
        SetupUser(sut, isAdmin: false);
        this._roleResolver.Setup(r => r.ResolveAsync("123456789"))
            .ReturnsAsync(new Pgan.PoracleWebNet.Api.Services.UserRoles(false, [WebhookUrl]));
        this._humanService.Setup(s => s.GetByIdAsync(WebhookUrl))
            .ReturnsAsync(new Human { Id = WebhookUrl, Name = "Raids", Type = "webhook", Enabled = 1, CurrentProfileNo = 1 });

        Assert.IsType<OkObjectResult>(await sut.ImpersonateById(new AdminController.ImpersonateRequest(WebhookUrl)));

        Assert.Contains($"impersonating {MaskedWebhook}", log.All, StringComparison.Ordinal);
        Assert.DoesNotContain(WebhookToken, log.All, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImpersonatingAPersonStillLogsTheirId()
    {
        var log = new CapturingLogger<AdminController>();
        var sut = this.CreateSut(log);
        SetupUser(sut, userId: "admin-1", isAdmin: true);
        this._humanService.Setup(s => s.GetByIdAsync("555"))
            .ReturnsAsync(new Human { Id = "555", Name = "Someone", Type = "discord:user", Enabled = 1, CurrentProfileNo = 1 });

        Assert.IsType<OkObjectResult>(await sut.ImpersonateById(new AdminController.ImpersonateRequest("555")));

        Assert.Contains("Admin admin-1 impersonating 555", log.All, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnAdminOpeningAWebhookFromTheUserListIsLoggedWithoutItsToken()
    {
        var log = new CapturingLogger<AdminController>();
        var sut = this.CreateSut(log);
        SetupUser(sut, isAdmin: true);
        this._humanService.Setup(s => s.GetByIdAsync(WebhookUrl))
            .ReturnsAsync(new Human { Id = WebhookUrl, Name = "Raids", Type = "webhook", Enabled = 1, CurrentProfileNo = 1 });

        Assert.IsType<OkObjectResult>(await sut.ImpersonateUser(WebhookUrl));

        Assert.Contains($"impersonating user {MaskedWebhook}", log.All, StringComparison.Ordinal);
        Assert.DoesNotContain(WebhookToken, log.All, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeletingAWebhookIsLoggedWithoutItsToken()
    {
        var log = new CapturingLogger<AdminController>();
        var sut = this.CreateSut(log);
        SetupUser(sut, isAdmin: true);
        this._userPurgeService.Setup(s => s.PurgeAsync(WebhookUrl)).ReturnsAsync(true);

        Assert.IsType<NoContentResult>(await sut.DeleteUser(WebhookUrl));

        Assert.Contains($"deleted user {MaskedWebhook}", log.All, StringComparison.Ordinal);
        Assert.DoesNotContain(WebhookToken, log.All, StringComparison.Ordinal);
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
    public async Task GetPoracleAdminsIncludesTelegramAndStillIncludesDiscord()
    {
        SetupUser(this._sut, isAdmin: true);
        this._proxy.Setup(p => p.GetConfigAsync()).ReturnsAsync(new PoracleConfig
        {
            Admins = new PoracleAdmins { Discord = ["discord_admin"], Telegram = ["-100200", "tg_admin"] }
        });

        var result = Assert.IsType<OkObjectResult>(await this._sut.GetPoracleAdmins());
        var admins = Assert.IsAssignableFrom<IEnumerable<string>>(result.Value).ToList();

        Assert.Contains("discord_admin", admins);
        Assert.Contains("-100200", admins);
        Assert.Contains("tg_admin", admins);
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
