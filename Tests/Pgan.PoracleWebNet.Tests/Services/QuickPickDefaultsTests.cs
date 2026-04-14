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

    private readonly QuickPickService _sut;

    public QuickPickDefaultsTests() => this._sut = new QuickPickService(
        new Mock<IQuickPickDefinitionRepository>().Object,
        new Mock<IQuickPickAppliedStateRepository>().Object,
        new Mock<IMonsterService>().Object,
        new Mock<IRaidService>().Object,
        new Mock<IEggService>().Object,
        new Mock<IQuestService>().Object,
        new Mock<IInvasionService>().Object,
        new Mock<ILureService>().Object,
        new Mock<INestService>().Object,
        new Mock<IGymService>().Object,
        new Mock<IMaxBattleService>().Object,
        new Mock<IMasterDataService>().Object,
        new Mock<ILogger<QuickPickService>>().Object);

    [Fact]
    public async Task DefaultInvasionPicksUseValidGruntTypes()
    {
        var defaults = await this._sut.GetDefaultPicksAsync();

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

    [Fact]
    public async Task ApplyRocketLeadersCreatesThreeInvasionsWithLeaderGruntTypes()
    {
        var definitionRepo = new Mock<IQuickPickDefinitionRepository>();
        var appliedRepo = new Mock<IQuickPickAppliedStateRepository>();
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

        var sut = new QuickPickService(
            definitionRepo.Object, appliedRepo.Object,
            new Mock<IMonsterService>().Object, new Mock<IRaidService>().Object,
            new Mock<IEggService>().Object, new Mock<IQuestService>().Object,
            invasionService.Object, new Mock<ILureService>().Object,
            new Mock<INestService>().Object, new Mock<IGymService>().Object,
            new Mock<IMaxBattleService>().Object, new Mock<IMasterDataService>().Object,
            new Mock<ILogger<QuickPickService>>().Object);

        await sut.ApplyAsync("user1", 1, "invasion-leader", new QuickPickApplyRequest());

        Assert.Equal(3, captured.Count);
        Assert.Equal(new[] { "cliff", "arlo", "sierra" }, captured.Select(i => i.GruntType).ToArray());
        Assert.All(captured, i => Assert.Equal(1, i.ProfileNo));
    }
}
