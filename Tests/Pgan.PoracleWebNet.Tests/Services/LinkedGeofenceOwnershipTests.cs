using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;
using Pgan.PoracleWebNet.Tests.Helpers;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>A drawn fence belongs to the person, not to one of their two ids.</summary>
public class LinkedGeofenceOwnershipTests
{
    private const string Discord = PairTestSupport.DiscordId;
    private const string Telegram = PairTestSupport.TelegramId;

    private readonly Mock<IUserGeofenceRepository> _repository = new();
    private readonly Mock<IUserAreaWriter> _areaWriter = new();
    private readonly Mock<IPoracleHumanProxy> _humanProxy = new();
    private readonly Mock<IPoracleApiProxy> _api = new();
    private readonly Mock<IFeatureGate> _gate = new();

    public LinkedGeofenceOwnershipTests()
    {
        this._gate.Setup(g => g.IsEnabledAsync(It.IsAny<string>())).ReturnsAsync(true);
        this._areaWriter.Setup(w => w.AddToActiveProfileAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
        this._areaWriter.Setup(w => w.RemoveFromActiveProfileAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
        this._api.Setup(a => a.ReloadGeofencesAsync()).Returns(Task.CompletedTask);
        this._repository.Setup(r => r.GetByHumanIdAsync(It.IsAny<string>())).ReturnsAsync([]);
    }

    private UserGeofenceService Sut(Mock<ILinkedAccount> linked)
    {
        var kojiService = new Mock<IKojiService>();
        return new UserGeofenceService(
            this._repository.Object, kojiService.Object, this._api.Object, this._humanProxy.Object,
            Mock.Of<IHumanService>(), this._areaWriter.Object, linked.Object, PairTestSupport.Writer(linked),
            Mock.Of<IDiscordNotificationService>(), this._gate.Object, new ConfigurationBuilder().Build(),
            NullLogger<UserGeofenceService>.Instance);
    }

    private static Mock<ILinkedAccount> Pair(string caller) => PairTestSupport.Linked(PairTestSupport.Pair(caller));

    [Fact]
    public async Task Activate_AcceptsAFenceTheOtherIdDrew_AndSwitchesItOnForBothHumans()
    {
        this._repository.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(new UserGeofence { Id = 5, HumanId = Telegram, KojiName = "old town loop" });

        var warning = await this.Sut(Pair(Discord)).AddToProfileAsync(Discord, 1, 5);

        Assert.Null(warning);
        this._areaWriter.Verify(w => w.AddToActiveProfileAsync(Discord, "old town loop"), Times.Once);
        this._areaWriter.Verify(w => w.AddToActiveProfileAsync(Telegram, "old town loop"), Times.Once);
    }

    [Fact]
    public async Task Activate_StillRefusesAFenceDrawnByAnUnrelatedPerson()
    {
        this._repository.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(new UserGeofence { Id = 5, HumanId = "300000001", KojiName = "northside" });

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => this.Sut(Pair(Discord)).AddToProfileAsync(Discord, 1, 5));

        this._areaWriter.Verify(w => w.AddToActiveProfileAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Activate_Unlinked_StillRefusesAnotherIdsFence()
    {
        this._repository.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(new UserGeofence { Id = 5, HumanId = Telegram, KojiName = "riverside" });

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => this.Sut(PairTestSupport.Unlinked()).AddToProfileAsync(Discord, 1, 5));
    }

    [Fact]
    public async Task Activate_OtherSideRefusing_ReturnsAWarningAndKeepsTheFirstWrite()
    {
        this._repository.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(new UserGeofence { Id = 5, HumanId = Discord, KojiName = "riverside" });
        this._areaWriter.Setup(w => w.AddToActiveProfileAsync(Telegram, "riverside")).ThrowsAsync(new HttpRequestException("PoracleNG refused"));

        var warning = await this.Sut(Pair(Discord)).AddToProfileAsync(Discord, 1, 5);

        Assert.NotNull(warning);
        Assert.Equal(LinkSide.Telegram, warning!.Side);
    }

    [Fact]
    public async Task Create_CapCountsBothIdsFences_TheEleventhIsRefused()
    {
        this._repository.Setup(r => r.GetCountByHumanIdAsync(Discord)).ReturnsAsync(6);
        this._repository.Setup(r => r.GetCountByHumanIdAsync(Telegram)).ReturnsAsync(4);
        var model = new UserGeofenceCreate { DisplayName = "Old Town", Polygon = [[1.0, 2.0], [3.0, 4.0], [5.0, 6.0]] };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => this.Sut(Pair(Discord)).CreateAsync(Discord, 1, model));

        Assert.Contains("Maximum of 10", ex.Message);
    }

    [Fact]
    public async Task Create_NineAcrossThePair_StillAllowsTheTenth()
    {
        this._repository.Setup(r => r.GetCountByHumanIdAsync(Discord)).ReturnsAsync(5);
        this._repository.Setup(r => r.GetCountByHumanIdAsync(Telegram)).ReturnsAsync(4);
        this._repository.Setup(r => r.GetAllAsync()).ReturnsAsync([]);
        this._repository.Setup(r => r.CreateAsync(It.IsAny<UserGeofence>())).ReturnsAsync((UserGeofence g) => g);
        var model = new UserGeofenceCreate { DisplayName = "Old Town", Polygon = [[1.0, 2.0], [3.0, 4.0], [5.0, 6.0]] };

        var created = await this.Sut(Pair(Discord)).CreateAsync(Discord, 1, model);

        Assert.Equal("old town", created.KojiName);
        Assert.Null(created.PairWarning);
        this._areaWriter.Verify(w => w.AddToActiveProfileAsync(Telegram, "old town"), Times.Once);
    }

    [Fact]
    public async Task Create_Unlinked_CapStaysTenOnOneId()
    {
        this._repository.Setup(r => r.GetCountByHumanIdAsync(Discord)).ReturnsAsync(10);
        var model = new UserGeofenceCreate { DisplayName = "Old Town", Polygon = [[1.0, 2.0], [3.0, 4.0], [5.0, 6.0]] };

        await Assert.ThrowsAsync<InvalidOperationException>(() => this.Sut(PairTestSupport.Unlinked()).CreateAsync(Discord, 1, model));
    }

    [Fact]
    public async Task Policy_OwnDrawnFenceOfEitherIdIsAllowed()
    {
        var human = new Mock<IPoracleHumanProxy>();
        human.Setup(p => p.GetAreaListAsync(Discord)).ReturnsAsync([]);
        human.Setup(p => p.GetAvailableAreasAsync(Discord)).ReturnsAsync([]);
        var fences = new Mock<IUserGeofenceRepository>();
        fences.Setup(r => r.GetByHumanIdAsync(Discord)).ReturnsAsync([]);
        fences.Setup(r => r.GetByHumanIdAsync(Telegram)).ReturnsAsync([new UserGeofence { HumanId = Telegram, KojiName = "Old Town Loop" }]);
        var links = new Mock<IAccountLinkRepository>();
        links.Setup(l => l.GetByHumanIdAsync(Discord)).ReturnsAsync(PairTestSupport.Pair(Discord).Link);

        var linked = new UserAreaPolicy(human.Object, fences.Object, links.Object, this._gate.Object);
        var unlinked = new UserAreaPolicy(human.Object, fences.Object, Mock.Of<IAccountLinkRepository>(), this._gate.Object);

        Assert.Contains("old town loop", await linked.AllowedKeysAsync(Discord));
        Assert.DoesNotContain("old town loop", await unlinked.AllowedKeysAsync(Discord));
    }

    // --- Area clean-up reaches both humans of a pair ---

    [Fact]
    public async Task Delete_Linked_RemovesTheNameFromBothHumansBeforeTheRowGoes()
    {
        this._repository.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(new UserGeofence { Id = 1, HumanId = Discord, KojiName = "riverside" });

        var warning = await this.Sut(Pair(Discord)).DeleteAsync(Telegram, 1, 1, deleteWidenedAlarms: false);

        Assert.Null(warning);
        this._areaWriter.Verify(w => w.RemoveEverywhereAsync(Telegram, "riverside", false), Times.Once);
        this._areaWriter.Verify(w => w.RemoveEverywhereAsync(Discord, "riverside", false), Times.Once);
        this._repository.Verify(r => r.DeleteAsync(1), Times.Once);
    }

    [Fact]
    public async Task Delete_Linked_SecondSideFailing_IsAWarningAndTheRowStillGoes()
    {
        this._repository.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(new UserGeofence { Id = 1, HumanId = Discord, KojiName = "riverside" });
        this._areaWriter.Setup(w => w.RemoveEverywhereAsync(Telegram, "riverside", false)).ThrowsAsync(new HttpRequestException("down"));

        var warning = await this.Sut(Pair(Discord)).DeleteAsync(Discord, 1, 1, deleteWidenedAlarms: false);

        Assert.Equal(LinkSide.Telegram, warning!.Side);
        this._repository.Verify(r => r.DeleteAsync(1), Times.Once);
    }

    [Fact]
    public async Task Delete_Linked_SignedInSideFailing_KeepsTheRowAndTriesNoSecond()
    {
        this._repository.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(new UserGeofence { Id = 1, HumanId = Discord, KojiName = "riverside" });
        this._areaWriter.Setup(w => w.RemoveEverywhereAsync(Discord, "riverside", false)).ThrowsAsync(new HttpRequestException("down"));

        await Assert.ThrowsAsync<HttpRequestException>(() => this.Sut(Pair(Discord)).DeleteAsync(Discord, 1, 1, false));

        this._areaWriter.Verify(w => w.RemoveEverywhereAsync(Telegram, It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
        this._repository.Verify(r => r.DeleteAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task Delete_Unlinked_IsOneRemoval()
    {
        this._repository.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(new UserGeofence { Id = 1, HumanId = Discord, KojiName = "riverside" });

        var warning = await this.Sut(PairTestSupport.Unlinked()).DeleteAsync(Discord, 1, 1, deleteWidenedAlarms: true);

        Assert.Null(warning);
        this._areaWriter.Verify(w => w.RemoveEverywhereAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Once);
    }

    [Fact]
    public async Task DeleteImpact_Linked_SumsAcrossThePair()
    {
        this._repository.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(new UserGeofence { Id = 1, HumanId = Discord, KojiName = "riverside" });
        this._areaWriter.Setup(w => w.PreviewRemovalAsync(Discord, "riverside"))
            .ReturnsAsync(new AreaDeleteImpact(new Dictionary<string, int> { ["pokemon"] = 2, ["raid"] = 1 }, 3));
        this._areaWriter.Setup(w => w.PreviewRemovalAsync(Telegram, "riverside"))
            .ReturnsAsync(new AreaDeleteImpact(new Dictionary<string, int> { ["pokemon"] = 4 }, 4));

        var impact = await this.Sut(Pair(Discord)).GetDeleteImpactAsync(Discord, 1);

        Assert.Equal(7, impact.Total);
        Assert.Equal(6, impact.WidenedByType["pokemon"]);
        Assert.Equal(1, impact.WidenedByType["raid"]);
    }

    [Fact]
    public async Task DeleteImpact_Unlinked_IsTheSingleAnswer()
    {
        this._repository.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(new UserGeofence { Id = 1, HumanId = Discord, KojiName = "riverside" });
        var single = new AreaDeleteImpact(new Dictionary<string, int> { ["pokemon"] = 2 }, 2);
        this._areaWriter.Setup(w => w.PreviewRemovalAsync(Discord, "riverside")).ReturnsAsync(single);

        Assert.Same(single, await this.Sut(PairTestSupport.Unlinked()).GetDeleteImpactAsync(Discord, 1));
    }

    private void RenameRow() =>
        this._repository.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(new UserGeofence
        {
            Id = 1, HumanId = Discord, KojiName = "downtown", DisplayName = "Downtown", Status = "active",
        });

    [Fact]
    public async Task Rename_Linked_RenamesOnBothHumans()
    {
        this.RenameRow();
        this._repository.Setup(r => r.UpdateAsync(It.IsAny<UserGeofence>())).ReturnsAsync((UserGeofence g) => g);
        this._areaWriter.Setup(w => w.RenameEverywhereAsync(It.IsAny<string>(), "downtown", "uptown")).ReturnsAsync(true);

        var renamed = await this.Sut(Pair(Discord)).RenameAsync(Discord, 1, "Uptown", null, null);

        Assert.Null(renamed.PairWarning);
        this._areaWriter.Verify(w => w.RenameEverywhereAsync(Discord, "downtown", "uptown"), Times.Once);
        this._areaWriter.Verify(w => w.RenameEverywhereAsync(Telegram, "downtown", "uptown"), Times.Once);
    }

    [Fact]
    public async Task Rename_Linked_OtherSideRefusing_KeepsTheRenameWithAWarning()
    {
        this.RenameRow();
        this._repository.Setup(r => r.UpdateAsync(It.IsAny<UserGeofence>())).ReturnsAsync((UserGeofence g) => g);
        this._areaWriter.Setup(w => w.RenameEverywhereAsync(Discord, "downtown", "uptown")).ReturnsAsync(true);
        this._areaWriter.Setup(w => w.RenameEverywhereAsync(Telegram, "downtown", "uptown")).ReturnsAsync(false);

        var renamed = await this.Sut(Pair(Discord)).RenameAsync(Discord, 1, "Uptown", null, null);

        Assert.Equal("telegram", renamed.PairWarning!.Side);
        Assert.Equal("uptown", renamed.KojiName);
    }

    [Fact]
    public async Task Rename_Linked_SignedInSideRefusing_RollsBack()
    {
        this.RenameRow();
        this._repository.Setup(r => r.UpdateAsync(It.IsAny<UserGeofence>())).ReturnsAsync((UserGeofence g) => g);
        this._areaWriter.Setup(w => w.RenameEverywhereAsync(Discord, "downtown", "uptown")).ReturnsAsync(false);

        await Assert.ThrowsAsync<GeofenceRenameFailedException>(() => this.Sut(Pair(Discord)).RenameAsync(Discord, 1, "Uptown", null, null));
    }

    [Fact]
    public async Task Rename_Unlinked_IsOneRename()
    {
        this.RenameRow();
        this._repository.Setup(r => r.UpdateAsync(It.IsAny<UserGeofence>())).ReturnsAsync((UserGeofence g) => g);
        this._areaWriter.Setup(w => w.RenameEverywhereAsync(Discord, "downtown", "uptown")).ReturnsAsync(true);

        var renamed = await this.Sut(PairTestSupport.Unlinked()).RenameAsync(Discord, 1, "Uptown", null, null);

        Assert.Null(renamed.PairWarning);
        this._areaWriter.Verify(w => w.RenameEverywhereAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }
}
