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
    private readonly Mock<IHumanRepository> _humans = new();
    private readonly Mock<IUserGeofenceRepository> _geofences = new();
    private readonly Mock<IUserGeofenceService> _geofenceService = new();
    private readonly Mock<IWebhookDelegateRepository> _delegates = new();
    private readonly Mock<IQuickPickDefinitionRepository> _quickPicks = new();
    private readonly Mock<IQuickPickAppliedStateRepository> _appliedStates = new();
    private readonly Mock<IHumanService> _humanService = new();
    private readonly UserPurgeService _sut;

    public UserPurgeServiceTests()
    {
        this._humans.Setup(r => r.ExistsAsync("u1")).ReturnsAsync(true);
        this._humans.Setup(r => r.DeleteUserAsync("u1")).ReturnsAsync(true);
        this._geofences.Setup(r => r.GetByHumanIdAsync("u1")).ReturnsAsync([]);
        this._quickPicks.Setup(r => r.GetByOwnerAsync("u1")).ReturnsAsync([]);

        this._sut = new UserPurgeService(
            this._humans.Object,
            this._geofences.Object,
            this._geofenceService.Object,
            this._delegates.Object,
            this._quickPicks.Object,
            this._appliedStates.Object,
            this._humanService.Object,
            NullLogger<UserPurgeService>.Instance);
    }

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
        this._humans.Verify(r => r.DeleteUserAsync("u1"), Times.Once);
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
        this._humans.Setup(r => r.ExistsAsync("ghost")).ReturnsAsync(false);

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
        this._humans.Verify(r => r.DeleteUserAsync("u1"), Times.Once);
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
        this._humans.Setup(r => r.DeleteUserAsync("u1"))
            .Callback(() => order.Add("human")).ReturnsAsync(true);

        await this._sut.PurgeAsync("u1");

        Assert.Equal("human", order[^1]);
    }
}
