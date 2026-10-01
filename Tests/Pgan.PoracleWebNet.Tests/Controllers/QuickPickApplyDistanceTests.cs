using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Pgan.PoracleWebNet.Api.Controllers;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Controllers;

/// <summary>
/// A quick-pick apply with a radius no alarm accepts is refused as a radius, not as a filter.
/// </summary>
/// <remarks>
/// The radius comes from the apply dialog, not from the pick, yet the refusal read "This quick pick holds
/// a filter value the alarm does not accept", which sent the user to edit a pick that was fine. And on
/// five of the ten types the value was applied after the validation ran, so it was not refused at all.
/// </remarks>
public class QuickPickApplyDistanceTests : ControllerTestBase
{
    private readonly Mock<IQuickPickService> _quickPicks = new();
    private readonly QuickPickController _sut;

    public QuickPickApplyDistanceTests()
    {
        this._sut = new QuickPickController(this._quickPicks.Object, new Mock<ISiteSettingService>().Object);
        SetupUser(this._sut);
    }

    [Fact]
    public async Task ApplyRefusesARadiusBeyondTheBoundAndSaysSo()
    {
        var result = await this._sut.Apply("pick", new QuickPickApplyRequest { Distance = AlarmDistance.MaxMetres + 1 });

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        var message = JsonSerializer.Serialize(bad.Value);
        Assert.Contains("distance", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(AlarmDistance.MaxMetres.ToString(System.Globalization.CultureInfo.InvariantCulture), message, StringComparison.Ordinal);
        this._quickPicks.Verify(
            s => s.ApplyAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<QuickPickApplyRequest>()), Times.Never);
    }

    [Fact]
    public async Task ReapplyRefusesTheSameRadius()
    {
        var result = await this._sut.Reapply("pick", new QuickPickApplyRequest { Distance = 99_999_999 });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10_000_000)] // production holds a rule at this radius
    [InlineData(AlarmDistance.MaxMetres)]
    public async Task ARadiusWithinTheBoundIsApplied(int distance)
    {
        this._quickPicks
            .Setup(s => s.ApplyAsync(It.IsAny<string>(), It.IsAny<int>(), "pick", It.IsAny<QuickPickApplyRequest>()))
            .ReturnsAsync(new QuickPickAppliedState());

        Assert.IsType<OkObjectResult>(await this._sut.Apply("pick", new QuickPickApplyRequest { Distance = distance }));
    }

    // ── the service, reached without the controller's check ─────────────────

    public static TheoryData<string> AllTypes() =>
        ["monster", "raid", "egg", "quest", "invasion", "lure", "nest", "gym", "maxbattle"];

    [Theory]
    [MemberData(nameof(AllTypes))]
    public async Task EveryTypeRefusesAnOutOfRangeRadiusAsARadius(string alarmType)
    {
        var (service, _) = Service(alarmType);

        var ex = await Assert.ThrowsAsync<AlarmValidationException>(
            () => service.ApplyAsync("u1", 1, "pick", new QuickPickApplyRequest { Distance = 99_999_999 }));

        Assert.Contains("distance", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("holds a filter value", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(AllTypes))]
    public async Task EveryTypeStillAppliesAnOrdinaryRadius(string alarmType)
    {
        var (service, created) = Service(alarmType);

        await service.ApplyAsync("u1", 1, "pick", new QuickPickApplyRequest { Distance = 2000 });

        Assert.True(created.Count > 0, $"{alarmType}: nothing was created");
    }

    /// <summary>A pick of the given type with a plausible filter, over mocks that record what is created.</summary>
    private static (QuickPickService Service, List<object> Created) Service(string alarmType)
    {
        var created = new List<object>();
        var definitions = new Mock<IQuickPickDefinitionRepository>();
        var filters = alarmType switch
        {
            "monster" => new Dictionary<string, object?> { ["pokemonId"] = 25, ["minIv"] = 90 },
            "raid" => new Dictionary<string, object?> { ["pokemonId"] = 150, ["level"] = 9000 },
            "egg" => new Dictionary<string, object?> { ["level"] = 5 },
            "quest" => new Dictionary<string, object?> { ["rewardType"] = 7, ["reward"] = 25 },
            "invasion" => new Dictionary<string, object?> { ["gruntType"] = "water" },
            "lure" => new Dictionary<string, object?> { ["lureId"] = 501 },
            "nest" => new Dictionary<string, object?> { ["pokemonId"] = 25 },
            "gym" => new Dictionary<string, object?> { ["team"] = 4 },
            "maxbattle" => new Dictionary<string, object?> { ["pokemonId"] = 9000, ["level"] = 5 },
            _ => throw new ArgumentOutOfRangeException(nameof(alarmType)),
        };
        definitions.Setup(r => r.GetByIdAsync("pick")).ReturnsAsync(new QuickPickDefinition
        {
            Id = "pick", Name = "Pick", AlarmType = alarmType, Scope = "global", Enabled = true, Filters = filters,
        });

        var monsters = new Mock<IMonsterService>();
        monsters.Setup(s => s.CreateAsync("u1", It.IsAny<Monster>())).Callback<string, Monster>((_, m) => created.Add(m)).ReturnsAsync((string _, Monster m) => m);
        var raids = new Mock<IRaidService>();
        raids.Setup(s => s.CreateAsync("u1", It.IsAny<Raid>())).Callback<string, Raid>((_, m) => created.Add(m)).ReturnsAsync((string _, Raid m) => m);
        var eggs = new Mock<IEggService>();
        eggs.Setup(s => s.CreateAsync("u1", It.IsAny<Egg>())).Callback<string, Egg>((_, m) => created.Add(m)).ReturnsAsync((string _, Egg m) => m);
        var quests = new Mock<IQuestService>();
        quests.Setup(s => s.CreateAsync("u1", It.IsAny<Quest>())).Callback<string, Quest>((_, m) => created.Add(m)).ReturnsAsync((string _, Quest m) => m);
        var invasions = new Mock<IInvasionService>();
        invasions.Setup(s => s.CreateAsync("u1", It.IsAny<Invasion>())).Callback<string, Invasion>((_, m) => created.Add(m)).ReturnsAsync((string _, Invasion m) => m);
        invasions.Setup(s => s.BulkCreateAsync("u1", It.IsAny<IEnumerable<Invasion>>())).Callback<string, IEnumerable<Invasion>>((_, m) => created.AddRange(m)).ReturnsAsync((string _, IEnumerable<Invasion> m) => m);
        var lures = new Mock<ILureService>();
        lures.Setup(s => s.CreateAsync("u1", It.IsAny<Lure>())).Callback<string, Lure>((_, m) => created.Add(m)).ReturnsAsync((string _, Lure m) => m);
        var nests = new Mock<INestService>();
        nests.Setup(s => s.CreateAsync("u1", It.IsAny<Nest>())).Callback<string, Nest>((_, m) => created.Add(m)).ReturnsAsync((string _, Nest m) => m);
        var gyms = new Mock<IGymService>();
        gyms.Setup(s => s.CreateAsync("u1", It.IsAny<Gym>())).Callback<string, Gym>((_, m) => created.Add(m)).ReturnsAsync((string _, Gym m) => m);
        var maxBattles = new Mock<IMaxBattleService>();
        maxBattles.Setup(s => s.CreateAsync("u1", It.IsAny<MaxBattle>())).Callback<string, MaxBattle>((_, m) => created.Add(m)).ReturnsAsync((string _, MaxBattle m) => m);

        var gate = new Mock<IFeatureGate>();
        gate.Setup(g => g.EnsureEnabledAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        gate.Setup(g => g.IsEnabledAsync(It.IsAny<string>())).ReturnsAsync(true);

        var service = new QuickPickService(
            definitions.Object,
            new Mock<IQuickPickAppliedStateRepository>().Object,
            monsters.Object, raids.Object, eggs.Object, quests.Object, invasions.Object,
            lures.Object, nests.Object, gyms.Object, maxBattles.Object,
            new Mock<IMasterDataService>().Object,
            gate.Object,
            new Mock<ILogger<QuickPickService>>().Object);

        return (service, created);
    }
}
