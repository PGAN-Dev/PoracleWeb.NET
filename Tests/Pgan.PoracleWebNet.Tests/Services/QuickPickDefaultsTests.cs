using Microsoft.Extensions.Logging;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

public class QuickPickDefaultsTests
{
    // Valid PoracleNG grunt_type values from processor/internal/gamedata/grunts.go (TypeNameFromTemplate).
    // "mixed" is the untyped CHARACTER_GRUNT_MALE/FEMALE — it is NOT the leader trio.
    private static readonly HashSet<string> ValidGruntTypes =
    [
        "", "mixed", "cliff", "arlo", "sierra", "giovanni", "decoy",
        "bug", "dark", "dragon", "electric", "fairy", "fighting", "fire", "flying",
        "ghost", "grass", "ground", "ice", "metal", "normal", "poison", "psychic", "rock", "water",
    ];

    private readonly QuickPickService _sut = BuildSut();

    private static QuickPickService BuildSut(
        Mock<IQuickPickDefinitionRepository>? definitionRepo = null,
        Mock<IQuickPickAppliedStateRepository>? appliedRepo = null,
        Mock<IInvasionService>? invasionService = null,
        Mock<IMonsterService>? monsterService = null) => new(
            (definitionRepo ?? new Mock<IQuickPickDefinitionRepository>()).Object,
            (appliedRepo ?? new Mock<IQuickPickAppliedStateRepository>()).Object,
            (monsterService ?? new Mock<IMonsterService>()).Object,
            new Mock<IRaidService>().Object,
            new Mock<IEggService>().Object,
            new Mock<IQuestService>().Object,
            (invasionService ?? new Mock<IInvasionService>()).Object,
            new Mock<ILureService>().Object,
            new Mock<INestService>().Object,
            new Mock<IGymService>().Object,
            new Mock<IMaxBattleService>().Object,
            new Mock<IMasterDataService>().Object,
            FeatureGateAlwaysOn(),
            new Mock<ILogger<QuickPickService>>().Object);

    /// <summary>A gate with every feature on, so these tests exercise the pick logic itself.</summary>
    private static IFeatureGate FeatureGateAlwaysOn()
    {
        var gate = new Mock<IFeatureGate>();
        gate.Setup(g => g.EnsureEnabledAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        gate.Setup(g => g.IsEnabledAsync(It.IsAny<string>())).ReturnsAsync(true);
        return gate.Object;
    }

    [Fact]
    public async Task DefaultInvasionPicksUseValidGruntTypes()
    {
        var defaults = (await this._sut.GetDefaultPicksAsync()).ToList();

        // Sentinel: guard against a refactor that accidentally drops the seed list.
        Assert.Contains(defaults, p => p.Id == "invasion-leader");
        Assert.Contains(defaults, p => p.Id == "invasion-giovanni");
        Assert.Contains(defaults, p => p.Id == "all-invasions");

        foreach (var pick in defaults.Where(p => p.AlarmType == "invasion"))
        {
            if (!pick.Filters.TryGetValue("gruntType", out var raw) || raw == null)
            {
                continue;
            }

            var value = raw.ToString() ?? "";
            Assert.True(
                ValidGruntTypes.Contains(value),
                $"Quick pick '{pick.Id}' has gruntType='{value}' which is not a known PoracleNG grunt_type.");
        }
    }

    [Fact]
    public async Task RocketLeadersPickDoesNotCarryGruntTypeFilter()
    {
        // Regression for #221: the Rocket Leaders pick must not set gruntType itself — the
        // fan-out in ApplyInvasionAsync is what assigns cliff/arlo/sierra per alarm.
        var defaults = await this._sut.GetDefaultPicksAsync();
        var leader = defaults.Single(p => p.Id == "invasion-leader");

        Assert.False(
            leader.Filters.TryGetValue("gruntType", out var raw) && raw != null,
            $"invasion-leader.Filters must not set gruntType (found '{raw}').");
    }

    [Fact]
    public async Task GiovanniPickExistsWithCorrectGruntType()
    {
        var defaults = await this._sut.GetDefaultPicksAsync();
        var giovanni = defaults.SingleOrDefault(p => p.Id == "invasion-giovanni");

        Assert.NotNull(giovanni);
        Assert.Equal("giovanni", giovanni!.Filters["gruntType"]?.ToString());
    }


    /// <summary>
    /// Captures the monsters one applied pick would create.
    /// </summary>
    private async Task<List<Monster>> ApplyMonsterPickAsync(string pickId)
    {
        var definitionRepo = new Mock<IQuickPickDefinitionRepository>();
        var monsterService = new Mock<IMonsterService>();

        var pick = (await this._sut.GetDefaultPicksAsync()).Single(p => p.Id == pickId);
        definitionRepo.Setup(r => r.GetByIdAsync(pickId)).ReturnsAsync(pick);

        // A pick naming no specific species creates one row through CreateAsync; one that excludes or
        // names species fans out through BulkCreateAsync. Both are captured so a test does not silently
        // assert nothing when a pick takes the other path.
        List<Monster> captured = [];
        monsterService.Setup(s => s.BulkCreateAsync("user1", It.IsAny<IEnumerable<Monster>>()))
            .Callback<string, IEnumerable<Monster>>((_, models) => captured = models.ToList())
            .ReturnsAsync((string _, IEnumerable<Monster> models) => models);
        monsterService.Setup(s => s.CreateAsync("user1", It.IsAny<Monster>()))
            .Callback<string, Monster>((_, model) => captured = [model])
            .ReturnsAsync((string _, Monster model) => model);

        var sut = BuildSut(definitionRepo: definitionRepo, monsterService: monsterService);
        await sut.ApplyAsync("user1", 1, pickId, new QuickPickApplyRequest());

        Assert.NotEmpty(captured);
        return captured;
    }

    [Fact]
    public async Task AnAppliedPickDoesNotCapTheAlarmAtTheOldLevelCeiling()
    {
        // "Level 30+ Pokemon — track all high-level wild spawns (weather boosted)" was applied with a
        // max level of 40, the game's cap until 2020, so it asked for 30-40 rather than 30 and up. None
        // of the 30 definitions sets maxLevel, so every quick pick carried it: 4,088 rules in production.
        var monsters = await this.ApplyMonsterPickAsync("high-level");

        Assert.All(monsters, m => Assert.Equal(55, m.MaxLevel));
        Assert.All(monsters, m => Assert.Equal(30, m.MinLevel));
    }

    [Fact]
    public async Task AnAppliedPickLeavesTheRankWindowUnbounded()
    {
        // 4096 is the column's no-bound value; 100 is a top-100 filter. It was inert where it landed,
        // because these default to no league, but an alarm later edited to add one inherited a window
        // nobody chose.
        var monsters = await this.ApplyMonsterPickAsync("high-iv");

        Assert.All(monsters, m => Assert.Equal(1, m.PvpRankingBest));
        Assert.All(monsters, m => Assert.Equal(4096, m.PvpRankingWorst));
        Assert.All(monsters, m => Assert.Equal(0, m.PvpRankingLeague));
    }

    [Fact]
    public async Task APickThatWantsARankWindowStillGetsIt()
    {
        // The legitimate-case half. Five definitions set a league, and all five set pvpRankingWorst
        // beside it, so the correction above cannot reach them — their filters overlay the defaults.
        var monsters = await this.ApplyMonsterPickAsync("pvp-great-1");

        Assert.All(monsters, m => Assert.Equal(1500, m.PvpRankingLeague));
        Assert.All(monsters, m => Assert.Equal(1, m.PvpRankingWorst));
        Assert.All(monsters, m => Assert.Equal(1, m.PvpRankingBest));
    }

    [Fact]
    public async Task AnAppliedPickAgreesWithWhatTheAddDialogWouldHaveCreated()
    {
        // The guard for the next drift. These defaults are meant to be MonsterCreate's, and the comment
        // said so while two of them had wandered off. Comparing against the type rather than against
        // literals means a change to one has to be made to both.
        var reference = new MonsterCreate();
        var monsters = await this.ApplyMonsterPickAsync("high-iv");

        foreach (var monster in monsters)
        {
            Assert.Equal(reference.MaxIv, monster.MaxIv);
            Assert.Equal(reference.MaxCp, monster.MaxCp);
            Assert.Equal(reference.MaxLevel, monster.MaxLevel);
            Assert.Equal(reference.MaxWeight, monster.MaxWeight);
            Assert.Equal(reference.MaxAtk, monster.MaxAtk);
            Assert.Equal(reference.MaxDef, monster.MaxDef);
            Assert.Equal(reference.MaxSta, monster.MaxSta);
            Assert.Equal(reference.PvpRankingBest, monster.PvpRankingBest);
            Assert.Equal(reference.PvpRankingWorst, monster.PvpRankingWorst);
        }
    }

    [Fact]
    public async Task ApplyRocketLeadersCreatesThreeInvasionsWithLeaderGruntTypes()
    {
        var definitionRepo = new Mock<IQuickPickDefinitionRepository>();
        var invasionService = new Mock<IInvasionService>();

        var defaults = await this._sut.GetDefaultPicksAsync();
        var leader = defaults.Single(p => p.Id == "invasion-leader");
        definitionRepo.Setup(r => r.GetByIdAsync("invasion-leader")).ReturnsAsync(leader);

        List<Invasion> captured = [];
        invasionService.Setup(s => s.BulkCreateAsync("user1", It.IsAny<IEnumerable<Invasion>>()))
            .Callback<string, IEnumerable<Invasion>>((_, models) => captured = models.ToList())
            .ReturnsAsync((string _, IEnumerable<Invasion> models) =>
            {
                var i = 100;
                foreach (var m in models)
                {
                    m.Uid = i++;
                }
                return models;
            });

        var sut = BuildSut(definitionRepo: definitionRepo, invasionService: invasionService);

        await sut.ApplyAsync("user1", 1, "invasion-leader", new QuickPickApplyRequest());

        Assert.Equal(3, captured.Count);
        Assert.Equal(
            new HashSet<string> { "arlo", "cliff", "sierra" },
            captured.Select(i => i.GruntType ?? "").ToHashSet());
        Assert.All(captured, i => Assert.Equal(1, i.ProfileNo));
    }
}
