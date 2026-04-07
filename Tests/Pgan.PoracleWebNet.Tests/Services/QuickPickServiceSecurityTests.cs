using Microsoft.Extensions.Logging;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

public class QuickPickServiceSecurityTests
{
    private readonly Mock<IQuickPickDefinitionRepository> _definitionRepository = new();
    private readonly Mock<IQuickPickAppliedStateRepository> _appliedStateRepository = new();
    private readonly Mock<IMonsterService> _monsterService = new();
    private readonly Mock<IRaidService> _raidService = new();
    private readonly Mock<IEggService> _eggService = new();
    private readonly Mock<IQuestService> _questService = new();
    private readonly Mock<IInvasionService> _invasionService = new();
    private readonly Mock<ILureService> _lureService = new();
    private readonly Mock<INestService> _nestService = new();
    private readonly Mock<IGymService> _gymService = new();
    private readonly Mock<IMaxBattleService> _maxBattleService = new();
    private readonly Mock<IMasterDataService> _masterDataService = new();
    private readonly Mock<ILogger<QuickPickService>> _logger = new();
    private readonly QuickPickService _sut;

    public QuickPickServiceSecurityTests() => this._sut = new QuickPickService(
            this._definitionRepository.Object,
            this._appliedStateRepository.Object,
            this._monsterService.Object,
            this._raidService.Object,
            this._eggService.Object,
            this._questService.Object,
            this._invasionService.Object,
            this._lureService.Object,
            this._nestService.Object,
            this._gymService.Object,
            this._maxBattleService.Object,
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
        this._definitionRepository.Setup(r => r.GetByIdAsync(definition.Id))
            .ReturnsAsync(definition);

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

    [Fact]
    public async Task RemoveAsyncPassesCallerUserIdToServiceDeletes()
    {
        // Arrange: an applied state with tracked UIDs
        var quickPickId = Guid.NewGuid().ToString();
        var appliedState = new QuickPickAppliedState
        {
            UserId = "real_user",
            ProfileNo = 1,
            QuickPickId = quickPickId,
            AlarmType = "monster",
            TrackedUids = [10, 20, 30],
        };
        this._appliedStateRepository.Setup(r => r.GetAsync("real_user", 1, quickPickId))
            .ReturnsAsync(appliedState);

        // Act
        await this._sut.RemoveAsync("real_user", 1, quickPickId);

        // Assert: DeleteAsync is called with the caller's userId, not some other user
        this._monsterService.Verify(s => s.DeleteAsync("real_user", 10), Times.Once);
        this._monsterService.Verify(s => s.DeleteAsync("real_user", 20), Times.Once);
        this._monsterService.Verify(s => s.DeleteAsync("real_user", 30), Times.Once);
        this._appliedStateRepository.Verify(r => r.DeleteAsync("real_user", 1, quickPickId), Times.Once);
    }

    [Fact]
    public async Task RemoveAsyncReturnsFalseWhenAppliedStateNotFound()
    {
        this._appliedStateRepository.Setup(r => r.GetAsync("user1", 1, "missing"))
            .ReturnsAsync((QuickPickAppliedState?)null);

        var result = await this._sut.RemoveAsync("user1", 1, "missing");

        Assert.False(result);
        this._monsterService.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task DeleteUserPickAsyncRejectsDeleteForNonOwner()
    {
        // Arrange: definition owned by another user
        this._definitionRepository.Setup(r => r.GetByIdAndOwnerAsync("pick1", "attacker"))
            .ReturnsAsync((QuickPickDefinition?)null);

        // Act
        var result = await this._sut.DeleteUserPickAsync("attacker", "pick1");

        // Assert: returns false and does not delete
        Assert.False(result);
        this._definitionRepository.Verify(r => r.DeleteAsync(It.IsAny<string>()), Times.Never);
        this._definitionRepository.Verify(r => r.DeleteByIdAndOwnerAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task DeleteUserPickAsyncAllowsDeleteForOwner()
    {
        var definition = new QuickPickDefinition
        {
            Id = "pick1",
            Name = "My Pick",
            AlarmType = "monster",
            Scope = "user",
            OwnerUserId = "owner1",
        };
        this._definitionRepository.Setup(r => r.GetByIdAndOwnerAsync("pick1", "owner1"))
            .ReturnsAsync(definition);

        var result = await this._sut.DeleteUserPickAsync("owner1", "pick1");

        Assert.True(result);
        this._definitionRepository.Verify(r => r.DeleteByIdAndOwnerAsync("pick1", "owner1"), Times.Once);
    }

    [Fact]
    public async Task RemoveAsyncPassesCallerUserIdForRaidDeletes()
    {
        var quickPickId = Guid.NewGuid().ToString();
        var appliedState = new QuickPickAppliedState
        {
            UserId = "real_user",
            ProfileNo = 1,
            QuickPickId = quickPickId,
            AlarmType = "raid",
            TrackedUids = [5],
        };
        this._appliedStateRepository.Setup(r => r.GetAsync("real_user", 1, quickPickId))
            .ReturnsAsync(appliedState);

        await this._sut.RemoveAsync("real_user", 1, quickPickId);

        this._raidService.Verify(s => s.DeleteAsync("real_user", 5), Times.Once);
    }
}
