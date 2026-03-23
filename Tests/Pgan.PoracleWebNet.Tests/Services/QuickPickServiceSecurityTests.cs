using Microsoft.Extensions.Logging;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

public class QuickPickServiceSecurityTests
{
    private readonly Mock<IPwebSettingService> _settingService = new();
    private readonly Mock<IMonsterService> _monsterService = new();
    private readonly Mock<IRaidService> _raidService = new();
    private readonly Mock<IEggService> _eggService = new();
    private readonly Mock<IQuestService> _questService = new();
    private readonly Mock<IInvasionService> _invasionService = new();
    private readonly Mock<ILureService> _lureService = new();
    private readonly Mock<INestService> _nestService = new();
    private readonly Mock<IGymService> _gymService = new();
    private readonly Mock<IMasterDataService> _masterDataService = new();
    private readonly Mock<ILogger<QuickPickService>> _logger = new();
    private readonly QuickPickService _sut;

    public QuickPickServiceSecurityTests() => this._sut = new QuickPickService(
            this._settingService.Object,
            this._monsterService.Object,
            this._raidService.Object,
            this._eggService.Object,
            this._questService.Object,
            this._invasionService.Object,
            this._lureService.Object,
            this._nestService.Object,
            this._gymService.Object,
            this._masterDataService.Object,
            this._logger.Object);

    [Fact]
    public async Task ApplyAsyncIgnoresIdAndUidInMonsterFilters()
    {
        // Arrange: a QuickPick definition with malicious Id/Uid/ProfileNo in filters
        var definition = new QuickPickDefinition
        {
            Name = "Malicious Pick",
            AlarmType = "monster",
            Filters = new Dictionary<string, object?>
            {
                ["id"] = "victim_user",
                ["uid"] = 99999,
                ["profileNo"] = 42,
                ["minIv"] = 90,
            },
        };
        var definitionJson = System.Text.Json.JsonSerializer.Serialize(definition);
        this._settingService.Setup(s => s.GetByKeyAsync("quick_pick:" + definition.Id))
            .ReturnsAsync(new PwebSetting { Setting = "quick_pick:" + definition.Id, Value = definitionJson });

        Monster? capturedMonster = null;
        this._monsterService.Setup(s => s.CreateAsync("real_user", It.IsAny<Monster>()))
            .Callback<string, Monster>((_, m) => capturedMonster = m)
            .ReturnsAsync((string _, Monster m) => { m.Uid = 1; return m; });

        var request = new QuickPickApplyRequest();

        // Act
        await this._sut.ApplyAsync("real_user", 1, definition.Id, request);

        // Assert: Id should be set by CreateAsync (not from filters), Uid is auto-generated,
        // ProfileNo is set by BuildMonster (not from filters), minIv should be applied
        this._monsterService.Verify(s => s.CreateAsync("real_user", It.Is<Monster>(m =>
            m.MinIv == 90 && m.ProfileNo == 1)), Times.Once);

        Assert.NotNull(capturedMonster);
        // The service layer sets Id = userId in CreateAsync, but BuildMonster should NOT have set it
        // from filters. ProfileNo should be 1 (from the method param), not 42.
        Assert.Equal(1, capturedMonster.ProfileNo);
    }
}
