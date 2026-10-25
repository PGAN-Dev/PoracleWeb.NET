using Microsoft.Extensions.Logging;
using Moq;
using Pgan.PoracleWebNet.Api.Controllers;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;
using Pgan.PoracleWebNet.Tests.Helpers;

namespace Pgan.PoracleWebNet.Tests.Controllers;

/// <summary>
/// The default destination is ignored while only one side of a pair has a human (spec section 1), so an Add
/// and a quick-pick apply with the default side missing land on the caller instead of answering 409.
/// </summary>
public class LinkedCreateFallbackTests : ControllerTestBase
{
    private const string Caller = "100000000000000001";

    private static ILinkedAccount DefaultSideMissing() =>
        PairTestSupport.Linked(PairTestSupport.Pair(Caller, telegramExists: false, defaultSide: LinkSide.Telegram)).Object;

    [Fact]
    public async Task Add_DefaultSideMissing_LandsOnTheCaller()
    {
        var service = new Mock<IMonsterService>();
        service.Setup(s => s.CreateAsync(Caller, It.IsAny<Monster>())).ReturnsAsync(new Monster { Uid = 5 });
        var linked = DefaultSideMissing();
        var sut = new MonsterController(service.Object, Mock.Of<IPokemonEditCapabilityService>(), LinkedTestKit.Router(linked));
        SetupUser(sut, Caller);

        await sut.Create(new MonsterCreate());

        service.Verify(s => s.CreateAsync(Caller, It.IsAny<Monster>()), Times.Once);
    }

    [Fact]
    public async Task Add_NamedSideMissing_StillAnswers409()
    {
        var router = LinkedTestKit.Router(DefaultSideMissing());

        var ex = await Assert.ThrowsAsync<AlarmDestinationException>(() => router.CreateTargetAsync(Caller, "telegram"));

        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public async Task QuickPickApply_DefaultSideMissing_LandsOnTheCaller()
    {
        var definitionRepo = new Mock<IQuickPickDefinitionRepository>();
        var monsterService = new Mock<IMonsterService>();
        var gate = new Mock<IFeatureGate>();
        gate.Setup(g => g.EnsureEnabledAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        gate.Setup(g => g.IsEnabledAsync(It.IsAny<string>())).ReturnsAsync(true);
        var linked = DefaultSideMissing();
        var humans = new Mock<IHumanService>();
        var sut = new QuickPickService(
            definitionRepo.Object, new Mock<IQuickPickAppliedStateRepository>().Object, monsterService.Object,
            new Mock<IRaidService>().Object, new Mock<IEggService>().Object, new Mock<IQuestService>().Object,
            new Mock<IInvasionService>().Object, new Mock<ILureService>().Object, new Mock<INestService>().Object,
            new Mock<IGymService>().Object, new Mock<IMaxBattleService>().Object, new Mock<IMasterDataService>().Object,
            gate.Object, linked, LinkedTestKit.Router(linked), humans.Object, new Mock<ILogger<QuickPickService>>().Object);
        var pick = (await sut.GetDefaultPicksAsync()).Single(p => p.Id == "high-level");
        definitionRepo.Setup(r => r.GetByIdAsync("high-level")).ReturnsAsync(pick);
        monsterService.Setup(s => s.CreateAsync(Caller, It.IsAny<Monster>())).ReturnsAsync((string _, Monster m) => m);
        monsterService.Setup(s => s.BulkCreateAsync(Caller, It.IsAny<IEnumerable<Monster>>()))
            .ReturnsAsync((string _, IEnumerable<Monster> m) => m);

        await sut.ApplyAsync(Caller, 1, "high-level", new QuickPickApplyRequest());

        monsterService.Verify(s => s.CreateAsync(Caller, It.IsAny<Monster>()), Times.Once);
    }
}
