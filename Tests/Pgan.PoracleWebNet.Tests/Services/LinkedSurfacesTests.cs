using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;
using static Pgan.PoracleWebNet.Tests.Helpers.LinkedTestKit;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// Cleaning, dashboard counts, profile overview, test alerts and quick picks across a linked pair, each
/// beside the unlinked call it must leave exactly as it was.
/// </summary>
public class LinkedSurfacesTests
{
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static Mock<IFeatureGate> Gate()
    {
        var gate = new Mock<IFeatureGate>();
        gate.Setup(g => g.IsEnabledAsync(It.IsAny<string>())).ReturnsAsync(true);
        gate.Setup(g => g.EnsureEnabledAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        return gate;
    }

    // ---- Cleaning ----

    private static (CleaningService Sut, Mock<IPoracleTrackingProxy> Proxy) Cleaning(ILinkedAccount linked)
    {
        var proxy = new Mock<IPoracleTrackingProxy>();
        var sut = new CleaningService(proxy.Object, Gate().Object, Mock.Of<IPoracleIncidentProxy>(), linked, NullLogger<CleaningService>.Instance);
        return (sut, proxy);
    }

    [Fact]
    public async Task Cleaning_Linked_SumsCounts()
    {
        var (sut, proxy) = Cleaning(Linked());
        proxy.Setup(p => p.GetAllTrackingAsync(Discord)).ReturnsAsync(Json("""{"raid":[{"uid":1,"clean":1}]}"""));
        proxy.Setup(p => p.GetAllTrackingAsync(Telegram)).ReturnsAsync(Json("""{"raid":[{"uid":2,"clean":0}],"gym":[{"uid":3,"clean":1}]}"""));

        var counts = await sut.GetCleanCountsAsync(Discord, 1);

        Assert.Equal(new CleanCount(1, 2), counts["raids"]);
        Assert.False(counts["raids"].IsAllClean());
        Assert.True(counts["gyms"].IsAllClean());
    }

    [Fact]
    public async Task Cleaning_Linked_CleanOnlyWhenBothAre()
    {
        var (sut, proxy) = Cleaning(Linked());
        proxy.Setup(p => p.GetAllTrackingAsync(Discord)).ReturnsAsync(Json("""{"raid":[{"uid":1,"clean":1}]}"""));
        proxy.Setup(p => p.GetAllTrackingAsync(Telegram)).ReturnsAsync(Json("""{"raid":[{"uid":2,"clean":1}]}"""));

        Assert.True((await sut.GetCleanCountsAsync(Discord, 1))["raids"].IsAllClean());
    }

    [Fact]
    public async Task Cleaning_Linked_TogglePatchesBothHumans()
    {
        var (sut, proxy) = Cleaning(Linked());
        proxy.Setup(p => p.GetByUserAsync("raid", Discord)).ReturnsAsync(Json("""[{"uid":1}]"""));
        proxy.Setup(p => p.GetByUserAsync("raid", Telegram)).ReturnsAsync(Json("""[{"uid":2}]"""));
        proxy.Setup(p => p.PatchAsync("raid", It.IsAny<string>(), It.IsAny<IReadOnlyList<int>>(), It.IsAny<TrackingPatchSet>()))
            .ReturnsAsync((string _, string _, IReadOnlyList<int> uids, TrackingPatchSet _) => new TrackingPatchResult([.. uids], [], []));

        var updated = await sut.ToggleCleanRaidsAsync(Discord, 1, 1);

        Assert.Equal(2, updated);
        proxy.Verify(p => p.PatchAsync("raid", Discord, It.Is<IReadOnlyList<int>>(u => u.SequenceEqual(new[] { 1 })), It.IsAny<TrackingPatchSet>()), Times.Once);
        proxy.Verify(p => p.PatchAsync("raid", Telegram, It.Is<IReadOnlyList<int>>(u => u.SequenceEqual(new[] { 2 })), It.IsAny<TrackingPatchSet>()), Times.Once);
    }

    [Fact]
    public async Task Cleaning_Unlinked_Unchanged()
    {
        var (sut, proxy) = Cleaning(Unlinked());
        proxy.Setup(p => p.GetAllTrackingAsync("u1")).ReturnsAsync(Json("""{"raid":[{"uid":1,"clean":1}]}"""));

        var counts = await sut.GetCleanCountsAsync("u1", 1);

        Assert.Equal(new CleanCount(1, 1), counts["raids"]);
        proxy.Verify(p => p.GetAllTrackingAsync(It.IsAny<string>()), Times.Once);
        proxy.Verify(p => p.GetAllTrackingAsync("u1"), Times.Once);
    }

    // ---- Dashboard ----

    [Fact]
    public async Task Dashboard_Linked_SumsCounts()
    {
        var proxy = new Mock<IPoracleTrackingProxy>();
        proxy.Setup(p => p.GetAllTrackingAsync(Discord)).ReturnsAsync(Json("""{"pokemon":[{},{}],"invasion":[{"grunt_type":"mixed"},{"grunt_type":"kecleon"}]}"""));
        proxy.Setup(p => p.GetAllTrackingAsync(Telegram)).ReturnsAsync(Json("""{"pokemon":[{}],"raid":[{}],"invasion":[{"grunt_type":"mixed"}]}"""));
        var sut = new DashboardService(proxy.Object, Gate().Object, Linked());

        var counts = await sut.GetCountsAsync(Discord, 1);

        Assert.Equal(3, counts.Monsters);
        Assert.Equal(1, counts.Raids);
        Assert.Equal(2, counts.Invasions);
        Assert.Equal(1, counts.PokestopEvents);
    }

    [Fact]
    public async Task Dashboard_Unlinked_OneRead()
    {
        var proxy = new Mock<IPoracleTrackingProxy>();
        proxy.Setup(p => p.GetAllTrackingAsync("u1")).ReturnsAsync(Json("""{"pokemon":[{},{}]}"""));
        var sut = new DashboardService(proxy.Object, Gate().Object, Unlinked());

        Assert.Equal(2, (await sut.GetCountsAsync("u1", 1)).Monsters);
        proxy.Verify(p => p.GetAllTrackingAsync(It.IsAny<string>()), Times.Once);
    }

    // ---- Profile overview ----

    private static ProfileOverviewService Overview(Mock<IPoracleTrackingProxy> proxy, ILinkedAccount linked) =>
        new(proxy.Object, Mock.Of<IPoracleHumanProxy>(), Gate().Object, Mock.Of<IPoracleIncidentProxy>(), linked);

    [Fact]
    public async Task ProfileOverview_Linked_ConcatenatesAndStamps()
    {
        var proxy = new Mock<IPoracleTrackingProxy>();
        proxy.Setup(p => p.GetAllTrackingAllProfilesAsync(Discord)).ReturnsAsync(Json("""{"pokemon":[{"uid":1,"profile_no":1}]}"""));
        proxy.Setup(p => p.GetAllTrackingAllProfilesAsync(Telegram)).ReturnsAsync(Json("""{"pokemon":[{"uid":2,"profile_no":1}],"raid":[{"uid":3,"profile_no":1}]}"""));

        var overview = await Overview(proxy, Linked()).GetAllProfilesOverviewAsync(Discord);

        var pokemon = overview.GetProperty("pokemon").EnumerateArray().ToList();
        Assert.Equal(["discord", "telegram"], pokemon.Select(r => r.GetProperty("destination").GetString()));
        Assert.Equal("telegram", overview.GetProperty("raid")[0].GetProperty("destination").GetString());
    }

    [Fact]
    public async Task ProfileOverview_Unlinked_PassesThroughUntouched()
    {
        var proxy = new Mock<IPoracleTrackingProxy>();
        var snapshot = Json("""{"pokemon":[{"uid":1,"profile_no":1}]}""");
        proxy.Setup(p => p.GetAllTrackingAllProfilesAsync("u1")).ReturnsAsync(snapshot);

        var overview = await Overview(proxy, Unlinked()).GetAllProfilesOverviewAsync("u1");

        Assert.Equal(snapshot.GetRawText(), overview.GetRawText());
    }

    // ---- Test alerts ----

    private static (TestAlertService Sut, Mock<IPoracleApiProxy> Api, Mock<IPoracleTrackingProxy> Tracking, Mock<IPoracleHumanProxy> Humans) TestAlert(ILinkedAccount linked)
    {
        var api = new Mock<IPoracleApiProxy>();
        var tracking = new Mock<IPoracleTrackingProxy>();
        var humans = new Mock<IPoracleHumanProxy>();
        var builder = new Mock<ITestPayloadBuilder>();
        builder.Setup(b => b.CanBuild(It.IsAny<string>())).Returns(true);
        builder.Setup(b => b.BuildAsync(It.IsAny<TestPayloadContext>())).ReturnsAsync(new TestPayloadBuildResult("pokemon", []));
        var sut = new TestAlertService(api.Object, tracking.Object, humans.Object, [builder.Object], linked, NullLogger<TestAlertService>.Instance);
        return (sut, api, tracking, humans);
    }

    [Fact]
    public async Task TestAlert_RuleOnTelegram_SendsAsTelegram()
    {
        var (sut, api, tracking, humans) = TestAlert(Linked());
        tracking.Setup(t => t.GetByUserAsync("pokemon", Discord)).ReturnsAsync(Json("[]"));
        tracking.Setup(t => t.GetByUserAsync("pokemon", Telegram)).ReturnsAsync(Json("""[{"uid":7,"template":"1"}]"""));
        humans.Setup(h => h.GetHumanAsync(Discord)).ReturnsAsync(Json("""{"id":"100000000000000001","type":"discord:user"}"""));
        humans.Setup(h => h.GetHumanAsync(Telegram)).ReturnsAsync(Json("""{"id":"200000001","type":"telegram:user"}"""));

        await sut.SendTestAlertAsync(Discord, "pokemon", 7);

        api.Verify(a => a.SendTestAlertAsync(It.Is<TestAlertRequest>(r => r.Target.Id == Telegram && r.Target.Type == "telegram:user")), Times.Once);
    }

    [Fact]
    public async Task TestAlert_NeitherHasTheUid_NotFound()
    {
        var (sut, _, tracking, humans) = TestAlert(Linked());
        tracking.Setup(t => t.GetByUserAsync("pokemon", It.IsAny<string>())).ReturnsAsync(Json("[]"));
        humans.Setup(h => h.GetHumanAsync(It.IsAny<string>())).ReturnsAsync(Json("""{"type":"discord:user"}"""));

        await Assert.ThrowsAsync<KeyNotFoundException>(() => sut.SendTestAlertAsync(Discord, "pokemon", 7));
    }

    [Fact]
    public async Task TestAlert_Unlinked_OneLookup()
    {
        var (sut, api, tracking, humans) = TestAlert(Unlinked());
        tracking.Setup(t => t.GetByUserAsync("pokemon", "u1")).ReturnsAsync(Json("""[{"uid":7}]"""));
        humans.Setup(h => h.GetHumanAsync("u1")).ReturnsAsync(Json("""{"type":"discord:user"}"""));

        await sut.SendTestAlertAsync("u1", "pokemon", 7);

        tracking.Verify(t => t.GetByUserAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        api.Verify(a => a.SendTestAlertAsync(It.Is<TestAlertRequest>(r => r.Target.Id == "u1")), Times.Once);
    }

    // ---- Quick picks ----

    private sealed class QuickPickKit
    {
        public Mock<IQuickPickDefinitionRepository> Definitions = new();
        public Mock<IQuickPickAppliedStateRepository> Applied = new();
        public Mock<ILureService> Lures = new();
        public QuickPickService Sut = null!;

        public static QuickPickKit Create(ILinkedAccount linked, IHumanService humans)
        {
            var kit = new QuickPickKit();
            kit.Definitions.Setup(d => d.GetByIdAsync("lure-pick")).ReturnsAsync(Definition());
            kit.Definitions.Setup(d => d.GetAllGlobalAsync()).ReturnsAsync([Definition()]);
            kit.Definitions.Setup(d => d.GetByOwnerAsync(It.IsAny<string>())).ReturnsAsync([]);
            kit.Sut = new QuickPickService(
                kit.Definitions.Object, kit.Applied.Object,
                Mock.Of<IMonsterService>(), Mock.Of<IRaidService>(), Mock.Of<IEggService>(), Mock.Of<IQuestService>(),
                Mock.Of<IInvasionService>(), kit.Lures.Object, Mock.Of<INestService>(), Mock.Of<IGymService>(),
                Mock.Of<IMaxBattleService>(), Mock.Of<IMasterDataService>(), Gate().Object,
                linked, Router(linked), humans, NullLogger<QuickPickService>.Instance);
            return kit;
        }

        private static QuickPickDefinition Definition() => new()
        {
            Id = "lure-pick",
            Name = "Lures",
            Scope = "global",
            AlarmType = "lure",
            Enabled = true,
            Filters = [],
        };

        public void LureUids(string humanId, params int[] uids) =>
            this.Lures.Setup(l => l.GetByUidAsync(humanId, It.IsAny<int>()))
                .ReturnsAsync((string _, int uid) => uids.Contains(uid) ? new Lure { Uid = uid } : null);
    }

    [Fact]
    public async Task QuickPickApply_Linked_UsesDefault()
    {
        var kit = QuickPickKit.Create(Linked(LinkSide.Telegram), Humans(discordProfile: 1, telegramProfile: 4));
        kit.Lures.SetupSequence(l => l.GetByUserAsync(Telegram, 4)).ReturnsAsync([]).ReturnsAsync([new Lure { Uid = 9 }]);
        kit.Lures.Setup(l => l.CreateAsync(Telegram, It.IsAny<Lure>())).ReturnsAsync(new Lure { Uid = 9 });

        var state = await kit.Sut.ApplyAsync(Discord, 1, "lure-pick", new QuickPickApplyRequest());

        kit.Lures.Verify(l => l.CreateAsync(Telegram, It.IsAny<Lure>()), Times.Once);
        kit.Lures.Verify(l => l.CreateAsync(Discord, It.IsAny<Lure>()), Times.Never);
        Assert.Equal(Telegram, state.UserId);
        Assert.Equal(4, state.ProfileNo);
        Assert.Equal([9], state.TrackedUids);
    }

    [Fact]
    public async Task QuickPickApply_Unlinked_CallerAndTokenProfile()
    {
        var kit = QuickPickKit.Create(Unlinked(), Mock.Of<IHumanService>());
        kit.Lures.SetupSequence(l => l.GetByUserAsync("u1", 2)).ReturnsAsync([]).ReturnsAsync([new Lure { Uid = 9 }]);
        kit.Lures.Setup(l => l.CreateAsync("u1", It.IsAny<Lure>())).ReturnsAsync(new Lure { Uid = 9 });

        var state = await kit.Sut.ApplyAsync("u1", 2, "lure-pick", new QuickPickApplyRequest());

        Assert.Equal("u1", state.UserId);
        Assert.Equal(2, state.ProfileNo);
    }

    [Fact]
    public async Task QuickPickGetAll_RowOnEitherSide_IsAppliedWithCountsSummed()
    {
        var kit = QuickPickKit.Create(Linked(), Humans(discordProfile: 1, telegramProfile: 3));
        kit.Applied.Setup(a => a.GetAsync(Telegram, 3, "lure-pick")).ReturnsAsync(
            new QuickPickAppliedState { UserId = Telegram, ProfileNo = 3, QuickPickId = "lure-pick", AlarmType = "lure", TrackedUids = [5, 6] });
        kit.LureUids(Telegram, 5, 6);

        var picks = (await kit.Sut.GetAllAsync(Discord, 1)).ToList();

        Assert.NotNull(picks[0].AppliedState);
        Assert.Equal(2, picks[0].AppliedState!.TrackedUids.Count);

        kit.Applied.Setup(a => a.GetAsync(Discord, 1, "lure-pick")).ReturnsAsync(
            new QuickPickAppliedState { UserId = Discord, ProfileNo = 1, QuickPickId = "lure-pick", AlarmType = "lure", TrackedUids = [1] });
        kit.LureUids(Discord, 1);

        picks = [.. await kit.Sut.GetAllAsync(Discord, 1)];
        Assert.Equal(3, picks[0].AppliedState!.TrackedUids.Count);
    }

    [Fact]
    public async Task QuickPickGetAll_NoRowOnEitherSide_NotApplied()
    {
        var kit = QuickPickKit.Create(Linked(), Humans());

        var picks = (await kit.Sut.GetAllAsync(Discord, 1)).ToList();

        Assert.Null(picks[0].AppliedState);
    }

    [Fact]
    public async Task QuickPickRemove_RowsOnBothSides_RemovesBoth()
    {
        var kit = QuickPickKit.Create(Linked(), Humans(discordProfile: 1, telegramProfile: 3));
        kit.Applied.Setup(a => a.GetAsync(Discord, 1, "lure-pick")).ReturnsAsync(
            new QuickPickAppliedState { UserId = Discord, ProfileNo = 1, QuickPickId = "lure-pick", AlarmType = "lure", TrackedUids = [1] });
        kit.Applied.Setup(a => a.GetAsync(Telegram, 3, "lure-pick")).ReturnsAsync(
            new QuickPickAppliedState { UserId = Telegram, ProfileNo = 3, QuickPickId = "lure-pick", AlarmType = "lure", TrackedUids = [5] });

        Assert.True(await kit.Sut.RemoveAsync(Discord, 1, "lure-pick"));

        kit.Lures.Verify(l => l.DeleteAsync(Discord, 1), Times.Once);
        kit.Lures.Verify(l => l.DeleteAsync(Telegram, 5), Times.Once);
        kit.Applied.Verify(a => a.DeleteAsync(Discord, 1, "lure-pick"), Times.Once);
        kit.Applied.Verify(a => a.DeleteAsync(Telegram, 3, "lure-pick"), Times.Once);
    }

    [Fact]
    public async Task QuickPickRemove_RowOnOneSide_LeavesTheOtherAlone()
    {
        var kit = QuickPickKit.Create(Linked(), Humans(discordProfile: 1, telegramProfile: 3));
        kit.Applied.Setup(a => a.GetAsync(Telegram, 3, "lure-pick")).ReturnsAsync(
            new QuickPickAppliedState { UserId = Telegram, ProfileNo = 3, QuickPickId = "lure-pick", AlarmType = "lure", TrackedUids = [5] });

        Assert.True(await kit.Sut.RemoveAsync(Discord, 1, "lure-pick"));

        kit.Lures.Verify(l => l.DeleteAsync(Discord, It.IsAny<int>()), Times.Never);
        kit.Lures.Verify(l => l.DeleteAsync(Telegram, 5), Times.Once);
    }
}
