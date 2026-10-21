using System.Net;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// Deleting a user removed the humans row alone. Alarms, geofences, delegate grants and quick picks all
/// stayed — unreachable, so it looked deleted, until the same id was created again and adopted the lot.
/// See #510, #511, #512.
/// </summary>
public class UserPurgeServiceTests
{
    private readonly Mock<IUserGeofenceRepository> _geofences = new();
    private readonly Mock<IUserGeofenceService> _geofenceService = new();
    private readonly Mock<IWebhookDelegateRepository> _delegates = new();
    private readonly Mock<IQuickPickDefinitionRepository> _quickPicks = new();
    private readonly Mock<IQuickPickAppliedStateRepository> _appliedStates = new();
    private readonly Mock<IHumanService> _humanService = new();
    private readonly UserPurgeService _sut;

    public UserPurgeServiceTests()
    {
        this._humanService.Setup(s => s.ExistsAsync("u1")).ReturnsAsync(true);
        this._humanService.Setup(s => s.DeleteUserAsync("u1")).ReturnsAsync(true);
        this._geofences.Setup(r => r.GetByHumanIdAsync("u1")).ReturnsAsync([]);
        this._quickPicks.Setup(r => r.GetByOwnerAsync("u1")).ReturnsAsync([]);

        this._sut = this.CreateSut(this._humanService.Object);
    }

    private UserPurgeService CreateSut(IHumanService humanService) => new(
        this._geofences.Object,
        this._geofenceService.Object,
        this._delegates.Object,
        this._quickPicks.Object,
        this._appliedStates.Object,
        humanService,
        NullLogger<UserPurgeService>.Instance);

    [Fact]
    public async Task PurgeRemovesEverythingTheAccountOwns()
    {
        this._geofences.Setup(r => r.GetByHumanIdAsync("u1"))
            .ReturnsAsync([new UserGeofence { Id = 7, HumanId = "u1", KojiName = "zz" }]);
        this._quickPicks.Setup(r => r.GetByOwnerAsync("u1"))
            .ReturnsAsync([new QuickPickDefinition { Id = "p1", Name = "P", AlarmType = "monster", OwnerUserId = "u1" }]);

        Assert.True(await this._sut.PurgeAsync("u1"));

        this._humanService.Verify(s => s.DeleteAllAlarmsByUserAsync("u1"), Times.Once);
        // Through the service, not the repository: a promoted fence must also leave the shared Koji project,
        // and Poracle has to re-read the feed. See #511.
        this._geofenceService.Verify(s => s.AdminDeleteAsync("u1", 7), Times.Once);
        this._delegates.Verify(r => r.RemoveAllForIdAsync("u1"), Times.Once);
        this._appliedStates.Verify(r => r.DeleteByUserAsync("u1"), Times.Once);
        this._quickPicks.Verify(r => r.DeleteByIdAndOwnerAsync("p1", "u1"), Times.Once);
        this._humanService.Verify(s => s.DeleteUserAsync("u1"), Times.Once);
    }

    /// <summary>
    /// Grants naming the id as the delegate go too, not only those naming it as the webhook — otherwise a
    /// deleted user keeps rights over webhooks that still exist.
    /// </summary>
    [Fact]
    public async Task PurgeClearsGrantsInBothDirections()
    {
        await this._sut.PurgeAsync("u1");

        this._delegates.Verify(r => r.RemoveAllForIdAsync("u1"), Times.Once);
        this._delegates.Verify(r => r.RemoveAllForWebhookAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task AnUnknownUserIsReportedRatherThanPartiallyPurged()
    {
        this._humanService.Setup(s => s.ExistsAsync("ghost")).ReturnsAsync(false);

        Assert.False(await this._sut.PurgeAsync("ghost"));

        this._humanService.Verify(s => s.DeleteAllAlarmsByUserAsync(It.IsAny<string>()), Times.Never);
        this._delegates.Verify(r => r.RemoveAllForIdAsync(It.IsAny<string>()), Times.Never);
    }

    /// <summary>
    /// One unreachable dependency must not strand the rest, and must not leave an account that cannot be
    /// deleted. The failure is logged for the admin to clear by hand.
    /// </summary>
    [Fact]
    public async Task OneFailedStepDoesNotStopTheOthers()
    {
        this._humanService.Setup(s => s.DeleteAllAlarmsByUserAsync("u1"))
            .ThrowsAsync(new HttpRequestException("PoracleNG is down"));

        Assert.True(await this._sut.PurgeAsync("u1"));

        this._delegates.Verify(r => r.RemoveAllForIdAsync("u1"), Times.Once);
        this._humanService.Verify(s => s.DeleteUserAsync("u1"), Times.Once);
    }

    /// <summary>The humans row goes last, so a part-way failure leaves an account still visible.</summary>
    [Fact]
    public async Task TheAccountItselfIsRemovedLast()
    {
        var order = new List<string>();
        this._humanService.Setup(s => s.DeleteAllAlarmsByUserAsync("u1"))
            .Callback(() => order.Add("alarms")).ReturnsAsync(0);
        this._delegates.Setup(r => r.RemoveAllForIdAsync("u1"))
            .Callback(() => order.Add("delegates")).ReturnsAsync(0);
        this._humanService.Setup(s => s.DeleteUserAsync("u1"))
            .Callback(() => order.Add("human")).ReturnsAsync(true);

        await this._sut.PurgeAsync("u1");

        Assert.Equal("human", order[^1]);
    }

    // ---- Existence through PoracleNG, end to end (#994) ----------------------------------------------
    //
    // These run the real HumanService over the real PoracleHumanProxy, so what is under test is the whole
    // chain the admin's DELETE reaches: a status from PoracleNG in, a purge answer out. The existence check
    // used to read Poracle's humans table directly, only because the proxy answered null for any failure.

    [Fact]
    public async Task APoracleOutageIsNotReportedAsAnAccountAlreadyGone()
    {
        var sut = this.CreateSut(OverProxy(HttpStatusCode.ServiceUnavailable, """{"status":"error","message":"database error"}"""));

        // Not false: the controller turns false into 404, which told the admin the account was gone.
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => sut.PurgeAsync("u1"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
        // And nothing was half-erased on the strength of an answer nobody gave.
        this._delegates.Verify(r => r.RemoveAllForIdAsync(It.IsAny<string>()), Times.Never);
        this._appliedStates.Verify(r => r.DeleteByUserAsync(It.IsAny<string>()), Times.Never);
        this._geofences.Verify(r => r.GetByHumanIdAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task AnAccountPoracleSaysIsMissingIsStillReportedAsMissing()
    {
        var sut = this.CreateSut(OverProxy(HttpStatusCode.NotFound, """{"status":"error","message":"User not found"}"""));

        Assert.False(await sut.PurgeAsync("ghost"));

        this._delegates.Verify(r => r.RemoveAllForIdAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task AnAccountPoracleKnowsIsStillPurged()
    {
        // Every request answers 200: the existence read, the per-type alarm reads (an empty list) and the
        // v2 delete.
        var sut = this.CreateSut(OverProxy(HttpStatusCode.OK, """{"human":{"id":"u1"},"status":"ok"}"""));

        Assert.True(await sut.PurgeAsync("u1"));

        this._delegates.Verify(r => r.RemoveAllForIdAsync("u1"), Times.Once);
    }

    /// <summary>A real HumanService over a real PoracleHumanProxy on a PoracleNG without /api/v2.</summary>
    private static HumanService OverProxy(HttpStatusCode status, string body)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Poracle:ApiAddress"] = "http://localhost:3030",
                ["Poracle:ApiSecret"] = "test-secret",
            })
            .Build();

        var proxy = new PoracleHumanProxy(
            new HttpClient(new FixedReply(status, body)),
            config,
            PoracleHumanProxyTests.ServerProfile("5.1.0"),
            PoracleHumanProxyTests.V2Schema(),
            PoracleHumanProxyTests.AreaSecurityPolicy(),
            new MemoryCache(new MemoryCacheOptions()),
            Mock.Of<ILogger<PoracleHumanProxy>>());

        var tracking = new Mock<IPoracleTrackingProxy>();
        tracking.Setup(t => t.GetByUserAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(System.Text.Json.JsonDocument.Parse("[]").RootElement.Clone());

        return new HumanService(proxy, tracking.Object);
    }

    private sealed class FixedReply(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
    }
}
