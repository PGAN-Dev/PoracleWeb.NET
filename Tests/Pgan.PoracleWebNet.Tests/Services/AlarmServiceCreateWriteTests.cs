using System.Text.Json;
using Microsoft.Extensions.Logging;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// Creates rely on the proxy for collision protection: the proxy dry-runs a create against PoracleNG's
/// v2 surface and throws when it would take another alarm over, so the services add no guard of their own
/// and write nothing a second time.
/// </summary>
public class AlarmServiceCreateWriteTests
{
    private const string Takeover = "Another alarm of this type already uses those settings. Edit or remove that one instead.";

    private readonly Mock<IPoracleTrackingProxy> _proxy = new();
    private readonly Mock<IFeatureGate> _gate = new();
    private readonly Mock<ITrackedUidRemapper> _remapper = new();

    public AlarmServiceCreateWriteTests() =>
        this._gate.Setup(g => g.EnsureEnabledAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

    public static TheoryData<string> AllTypes() =>
        ["pokemon", "raid", "egg", "quest", "nest", "gym", "maxbattle", "fort", "lure", "invasion"];

    [Theory]
    [MemberData(nameof(AllTypes))]
    public async Task CreateTakeoverRefusalPropagatesAsConflict(string type)
    {
        this._proxy
            .Setup(p => p.CreateAsync(type, "u1", It.IsAny<JsonElement>()))
            .ThrowsAsync(new TrackingConflictException(type, Takeover));

        var ex = await Assert.ThrowsAsync<TrackingConflictException>(() => this.CreateAsync(type));

        Assert.Equal(Takeover, ex.Message);
        // The proxy refused, so the service must not try another way in.
        this._proxy.Verify(p => p.CreateAsync(type, "u1", It.IsAny<JsonElement>()), Times.Once);
        this._proxy.Verify(
            p => p.UpsertV1Async(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<JsonElement>()), Times.Never);
        this._proxy.Verify(
            p => p.UpdateByUidAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<JsonElement>()),
            Times.Never);
    }

    [Theory]
    [MemberData(nameof(AllTypes))]
    public async Task CreateThatInsertsReturnsTheNewUid(string type)
    {
        // The legitimate case beside the refusal: nothing in the services stands in the way of an insert.
        this._proxy
            .Setup(p => p.CreateAsync(type, "u1", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([77], 0, 0, 1) { RowUids = [77] });
        this._proxy
            .Setup(p => p.GetByUserAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(JsonSerializer.SerializeToElement(new[] { new { uid = 77, id = "u1" } }));

        Assert.Equal(77, await this.CreateAsync(type));
    }

    [Fact]
    public async Task BulkCreateUsesRowUidsForMonsters()
    {
        this.RowUids("pokemon", [11, 0, 12]);
        var sut = new MonsterService(this._proxy.Object, this._gate.Object, this._remapper.Object);

        var created = (await sut.BulkCreateAsync(
            "u1", [new Monster { PokemonId = 1 }, new Monster { PokemonId = 2 }, new Monster { PokemonId = 3 }])).ToList();

        Assert.Equal([11, 0, 12], created.Select(m => m.Uid));
    }

    [Fact]
    public async Task BulkCreateUsesRowUidsForInvasions()
    {
        this.RowUids("invasion", [11, 0, 12]);
        var sut = new InvasionService(
            this._proxy.Object, this._gate.Object, Mock.Of<ILogger<InvasionService>>(), this._remapper.Object, Mock.Of<IPoracleIncidentProxy>());

        var created = (await sut.BulkCreateAsync(
            "u1",
            [new Invasion { GruntType = "water" }, new Invasion { GruntType = "fire" }, new Invasion { GruntType = "dark" }])).ToList();

        Assert.Equal([11, 0, 12], created.Select(i => i.Uid));
    }

    [Fact]
    public async Task BulkCreateUsesRowUidsForMaxBattles()
    {
        this.RowUids("maxbattle", [11, 0, 12]);
        var sut = new MaxBattleService(
            this._proxy.Object, this._gate.Object, Mock.Of<ILogger<MaxBattleService>>(), this._remapper.Object);

        var created = (await sut.BulkCreateAsync(
            "u1", [new MaxBattle { PokemonId = 1 }, new MaxBattle { PokemonId = 2 }, new MaxBattle { PokemonId = 3 }])).ToList();

        Assert.Equal([11, 0, 12], created.Select(m => m.Uid));
    }

    [Fact]
    public async Task MaxBattleCreateDiffersByRadiusInserts()
    {
        // Beside an existing alarm that differs only by radius: the proxy inserts, the service files it.
        this._proxy
            .Setup(p => p.CreateAsync("maxbattle", "u1", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([6], 0, 0, 1) { RowUids = [6] });
        var sut = new MaxBattleService(
            this._proxy.Object, this._gate.Object, Mock.Of<ILogger<MaxBattleService>>(), this._remapper.Object);

        var created = await sut.CreateAsync("u1", new MaxBattle { PokemonId = 150, Distance = 900 });

        Assert.Equal(6, created.Uid);
        this._proxy.Verify(p => p.GetByUserAsync("maxbattle", "u1"), Times.Never);
    }

    [Fact]
    public async Task MaxBattleCreateIdenticalStillRefusedWithItsMessage()
    {
        this._proxy
            .Setup(p => p.CreateAsync("maxbattle", "u1", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([], 1, 0, 0) { RowUids = [0] });
        var sut = new MaxBattleService(
            this._proxy.Object, this._gate.Object, Mock.Of<ILogger<MaxBattleService>>(), this._remapper.Object);

        var ex = await Assert.ThrowsAsync<TrackingConflictException>(
            () => sut.CreateAsync("u1", new MaxBattle { PokemonId = 150 }));

        Assert.Equal("You already have an identical max battle alarm.", ex.Message);
    }

    /// <summary>
    /// "All invasions" fans out to one alarm per grunt type. When the user already holds some, the proxy
    /// skips those rows (uid 0) and the pick must track only what this apply added.
    /// </summary>
    [Fact]
    public async Task QuickPickAllInvasionsPartlyTrackedTracksOnlyNew()
    {
        var invasionService = new Mock<IInvasionService>();
        var definitions = new Mock<IQuickPickDefinitionRepository>();
        var applied = new Mock<IQuickPickAppliedStateRepository>();
        var sut = this.QuickPick(invasionService, definitions, applied);

        definitions.Setup(r => r.GetByIdAsync("all-invasions")).ReturnsAsync(new QuickPickDefinition
        {
            Id = "all-invasions",
            AlarmType = "invasion",
            Scope = "global",
            Filters = [],
        });
        // Uid 5 is a rule the user already holds; the proxy skipped it, so the bulk create reports 0 for it.
        invasionService.SetupSequence(s => s.GetByUserAsync("u1", 0))
            .ReturnsAsync([new Invasion { Uid = 5, GruntType = "water" }])
            .ReturnsAsync([new Invasion { Uid = 5, GruntType = "water" }, new Invasion { Uid = 21 }, new Invasion { Uid = 22 }]);
        invasionService.Setup(s => s.BulkCreateAsync("u1", It.IsAny<IEnumerable<Invasion>>()))
            .ReturnsAsync((string _, IEnumerable<Invasion> rows) =>
            {
                var list = rows.ToList();
                for (var i = 0; i < list.Count; i++)
                {
                    list[i].Uid = i == 1 ? 21 : i == 2 ? 22 : 0;
                }

                return list;
            });
        QuickPickAppliedState? saved = null;
        applied.Setup(r => r.CreateOrUpdateAsync(It.IsAny<QuickPickAppliedState>()))
            .Callback<QuickPickAppliedState>(s => saved = s)
            .Returns(Task.CompletedTask);

        await sut.ApplyAsync("u1", 0, "all-invasions", new QuickPickApplyRequest());

        Assert.NotNull(saved);
        Assert.Equal([21, 22], saved.TrackedUids.OrderBy(x => x));
    }

    private QuickPickService QuickPick(
        Mock<IInvasionService> invasion, Mock<IQuickPickDefinitionRepository> definitions, Mock<IQuickPickAppliedStateRepository> applied)
    {
        var featureGate = new Mock<IFeatureGate>();
        featureGate.Setup(g => g.EnsureEnabledAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        featureGate.Setup(g => g.IsEnabledAsync(It.IsAny<string>())).ReturnsAsync(true);

        return new QuickPickService(
            definitions.Object,
            applied.Object,
            Mock.Of<IMonsterService>(),
            Mock.Of<IRaidService>(),
            Mock.Of<IEggService>(),
            Mock.Of<IQuestService>(),
            invasion.Object,
            Mock.Of<ILureService>(),
            Mock.Of<INestService>(),
            Mock.Of<IGymService>(),
            Mock.Of<IMaxBattleService>(),
            Mock.Of<IMasterDataService>(),
            featureGate.Object,
            Mock.Of<ILogger<QuickPickService>>());
    }

    private void RowUids(string type, int[] rowUids) =>
        this._proxy
            .Setup(p => p.CreateAsync(type, "u1", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([.. rowUids.Where(u => u > 0).Select(u => (long)u)], 0, 0, 2) { RowUids = rowUids });

    private async Task<int> CreateAsync(string type) => type switch
    {
        "pokemon" => (await new MonsterService(this._proxy.Object, this._gate.Object, this._remapper.Object)
            .CreateAsync("u1", new Monster { PokemonId = 25 })).Uid,
        "raid" => (await new RaidService(this._proxy.Object, this._gate.Object, Mock.Of<ILogger<RaidService>>(), this._remapper.Object)
            .CreateAsync("u1", new Raid { PokemonId = 150 })).Uid,
        "egg" => (await new EggService(this._proxy.Object, this._gate.Object, Mock.Of<ILogger<EggService>>(), this._remapper.Object)
            .CreateAsync("u1", new Egg { Level = 5 })).Uid,
        "quest" => (await new QuestService(this._proxy.Object, this._gate.Object, Mock.Of<ILogger<QuestService>>(), this._remapper.Object)
            .CreateAsync("u1", new Quest { RewardType = 7, Reward = 25 })).Uid,
        "nest" => (await new NestService(this._proxy.Object, this._gate.Object, Mock.Of<ILogger<NestService>>(), this._remapper.Object)
            .CreateAsync("u1", new Nest { PokemonId = 25 })).Uid,
        "gym" => (await new GymService(this._proxy.Object, this._gate.Object, Mock.Of<ILogger<GymService>>(), this._remapper.Object)
            .CreateAsync("u1", new Gym { Team = 4 })).Uid,
        "maxbattle" => (await new MaxBattleService(this._proxy.Object, this._gate.Object, Mock.Of<ILogger<MaxBattleService>>(), this._remapper.Object)
            .CreateAsync("u1", new MaxBattle { PokemonId = 150 })).Uid,
        "fort" => (await new FortChangeService(this._proxy.Object, this._gate.Object, Mock.Of<ILogger<FortChangeService>>(), this._remapper.Object)
            .CreateAsync("u1", new FortChange { FortType = "gym" })).Uid,
        "lure" => (await new LureService(this._proxy.Object, this._gate.Object, Mock.Of<ILogger<LureService>>(), this._remapper.Object)
            .CreateAsync("u1", new Lure { LureId = 501 })).Uid,
        "invasion" => (await new InvasionService(this._proxy.Object, this._gate.Object, Mock.Of<ILogger<InvasionService>>(), this._remapper.Object, Mock.Of<IPoracleIncidentProxy>())
            .CreateAsync("u1", new Invasion { GruntType = "water" })).Uid,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "No fixture for this tracking type."),
    };
}
