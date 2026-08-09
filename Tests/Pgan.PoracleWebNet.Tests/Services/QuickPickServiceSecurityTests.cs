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
    private readonly Mock<IFeatureGate> _featureGate = new();
    private readonly Mock<ILogger<QuickPickService>> _logger = new();
    private readonly QuickPickService _sut;

    public QuickPickServiceSecurityTests()
    {
        this._featureGate.Setup(g => g.EnsureEnabledAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        this._featureGate.Setup(g => g.IsEnabledAsync(It.IsAny<string>())).ReturnsAsync(true);
        this._sut = new QuickPickService(
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
            this._featureGate.Object,
            this._logger.Object);
    }

    // --- Ownership on the write path ---
    // CreateOrUpdateAsync upserts on Id alone and the Id comes from the request body, so without an
    // ownership check a user could post a global pick's well-known id and take the row over.

    [Fact]
    public async Task SaveUserPickRejectsAnIdThatBelongsToAGlobalPick()
    {
        this._definitionRepository.Setup(r => r.GetByIdAsync("hundo"))
            .ReturnsAsync(new QuickPickDefinition { Id = "hundo", Scope = "global", OwnerUserId = null });

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            this._sut.SaveUserPickAsync("attacker", new QuickPickDefinition { Id = "hundo", Name = "HIJACKED" }));

        this._definitionRepository.Verify(r => r.CreateOrUpdateAsync(It.IsAny<QuickPickDefinition>()), Times.Never);
    }

    [Fact]
    public async Task SaveUserPickRejectsAnotherUsersPrivatePick()
    {
        this._definitionRepository.Setup(r => r.GetByIdAsync("victims-pick"))
            .ReturnsAsync(new QuickPickDefinition { Id = "victims-pick", Scope = "user", OwnerUserId = "victim" });

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            this._sut.SaveUserPickAsync("attacker", new QuickPickDefinition { Id = "victims-pick" }));

        this._definitionRepository.Verify(r => r.CreateOrUpdateAsync(It.IsAny<QuickPickDefinition>()), Times.Never);
    }

    [Fact]
    public async Task SaveUserPickAllowsOverwritingYourOwnPick()
    {
        this._definitionRepository.Setup(r => r.GetByIdAsync("mine"))
            .ReturnsAsync(new QuickPickDefinition { Id = "mine", Scope = "user", OwnerUserId = "owner" });

        var saved = await this._sut.SaveUserPickAsync("owner", new QuickPickDefinition { Id = "mine", Name = "Renamed" });

        Assert.Equal("user", saved.Scope);
        Assert.Equal("owner", saved.OwnerUserId);
        this._definitionRepository.Verify(r => r.CreateOrUpdateAsync(It.IsAny<QuickPickDefinition>()), Times.Once);
    }

    [Fact]
    public async Task SaveUserPickAllowsABrandNewId()
    {
        this._definitionRepository.Setup(r => r.GetByIdAsync("brand-new")).ReturnsAsync((QuickPickDefinition?)null);

        var saved = await this._sut.SaveUserPickAsync("owner", new QuickPickDefinition { Id = "brand-new" });

        Assert.Equal("owner", saved.OwnerUserId);
        this._definitionRepository.Verify(r => r.CreateOrUpdateAsync(It.IsAny<QuickPickDefinition>()), Times.Once);
    }

    // --- Ownership on the read/apply path ---

    [Fact]
    public async Task GetVisibleByIdReturnsGlobalPicksToAnyone()
    {
        this._definitionRepository.Setup(r => r.GetByIdAsync("hundo"))
            .ReturnsAsync(new QuickPickDefinition { Id = "hundo", Scope = "global" });

        Assert.NotNull(await this._sut.GetVisibleByIdAsync("anyone", "hundo"));
    }

    [Fact]
    public async Task GetVisibleByIdHidesAnotherUsersPrivatePick()
    {
        this._definitionRepository.Setup(r => r.GetByIdAsync("victims-pick"))
            .ReturnsAsync(new QuickPickDefinition { Id = "victims-pick", Scope = "user", OwnerUserId = "victim" });
        this._definitionRepository.Setup(r => r.GetByIdAndOwnerAsync("victims-pick", "attacker"))
            .ReturnsAsync((QuickPickDefinition?)null);

        Assert.Null(await this._sut.GetVisibleByIdAsync("attacker", "victims-pick"));
    }

    [Fact]
    public async Task GetVisibleByIdReturnsYourOwnPick()
    {
        var mine = new QuickPickDefinition { Id = "mine", Scope = "user", OwnerUserId = "owner" };
        this._definitionRepository.Setup(r => r.GetByIdAsync("mine")).ReturnsAsync(mine);
        this._definitionRepository.Setup(r => r.GetByIdAndOwnerAsync("mine", "owner")).ReturnsAsync(mine);

        Assert.NotNull(await this._sut.GetVisibleByIdAsync("owner", "mine"));
    }

    [Fact]
    public async Task ApplyAsyncRefusesAnotherUsersPrivatePick()
    {
        this._definitionRepository.Setup(r => r.GetByIdAsync("victims-pick"))
            .ReturnsAsync(new QuickPickDefinition { Id = "victims-pick", Scope = "user", OwnerUserId = "victim" });
        this._definitionRepository.Setup(r => r.GetByIdAndOwnerAsync("victims-pick", "attacker"))
            .ReturnsAsync((QuickPickDefinition?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            this._sut.ApplyAsync("attacker", 1, "victims-pick", null));

        this._monsterService.Verify(s => s.CreateAsync(It.IsAny<string>(), It.IsAny<Monster>()), Times.Never);
        this._monsterService.Verify(s => s.BulkCreateAsync(It.IsAny<string>(), It.IsAny<IEnumerable<Monster>>()), Times.Never);
    }

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

    /// <summary>
    /// Deleting a definition left its applied state behind, and nothing could reach it: the listing walks
    /// definitions. It leaked, and a pick re-created under the same id inherited a stale "applied" badge
    /// pointing at the old alarm uids. See #470.
    /// </summary>
    [Fact]
    public async Task DeleteUserPickAsyncClearsTheOwnersAppliedState()
    {
        this._definitionRepository.Setup(r => r.GetByIdAndOwnerAsync("pick1", "owner1"))
            .ReturnsAsync(new QuickPickDefinition
            {
                Id = "pick1",
                Name = "My Pick",
                AlarmType = "monster",
                Scope = "user",
                OwnerUserId = "owner1",
            });

        await this._sut.DeleteUserPickAsync("owner1", "pick1");

        this._appliedStateRepository.Verify(r => r.DeleteByQuickPickIdAsync("pick1", "owner1"), Times.Once);
    }

    /// <summary>
    /// A global pick can be applied by anyone, so every user's state for it goes with the definition.
    /// </summary>
    [Fact]
    public async Task DeleteAdminPickAsyncClearsAppliedStateForEveryUser()
    {
        this._definitionRepository.Setup(r => r.GetByIdAsync("global1"))
            .ReturnsAsync(new QuickPickDefinition
            {
                Id = "global1",
                Name = "Global Pick",
                AlarmType = "monster",
                Scope = "global",
            });

        await this._sut.DeleteAdminPickAsync("global1");

        this._appliedStateRepository.Verify(r => r.DeleteByQuickPickIdAsync("global1", null), Times.Once);
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

    // --- Generated ids (#413) ---
    // The create dialog has no id field and sends "", which was stored verbatim. Every id-bearing route
    // then collapsed to /api/quick-picks/ so the pick could not be deleted or applied through any path.

    [Fact]
    public async Task SaveAdminPickRefusesToTakeOverAUsersPrivatePick()
    {
        // Given an id it converted whatever it found into a global pick -- so an admin editing their own
        // personal pick republished it to everyone, and an admin could take over anybody's. See #631.
        this._definitionRepository.Setup(r => r.GetByIdAsync("someones-pick"))
            .ReturnsAsync(new QuickPickDefinition { Id = "someones-pick", Scope = "user", OwnerUserId = "u2" });

        await Assert.ThrowsAsync<AlarmValidationException>(
            () => this._sut.SaveAdminPickAsync(new QuickPickDefinition { Id = "someones-pick", Name = "Mine now" }));

        this._definitionRepository.Verify(r => r.CreateOrUpdateAsync(It.IsAny<QuickPickDefinition>()), Times.Never);
    }

    [Fact]
    public async Task SaveAdminPickStillUpdatesAnExistingGlobalPick()
    {
        this._definitionRepository.Setup(r => r.GetByIdAsync("raid-5star"))
            .ReturnsAsync(new QuickPickDefinition { Id = "raid-5star", Scope = "global" });

        var saved = await this._sut.SaveAdminPickAsync(new QuickPickDefinition { Id = "raid-5star", Name = "Five star" });

        Assert.Equal("global", saved.Scope);
        this._definitionRepository.Verify(r => r.CreateOrUpdateAsync(It.IsAny<QuickPickDefinition>()), Times.Once);
    }

    [Fact]
    public async Task SeedingDefaultsCreatesEveryBuiltInPick()
    {
        // Save-time filter validation (#604) rejected all-invasions and invasion-leader, whose filter
        // sets are empty on purpose, so seeding aborted partway and left a partial preset list behind
        // both the first-visit auto-seed and Reset to Defaults. See #637.
        this._definitionRepository.Setup(r => r.GetAllGlobalAsync()).ReturnsAsync([]);
        this._definitionRepository.Setup(r => r.GetByIdAsync(It.IsAny<string>())).ReturnsAsync((QuickPickDefinition?)null);
        var created = new List<string>();
        this._definitionRepository.Setup(r => r.CreateOrUpdateAsync(It.IsAny<QuickPickDefinition>()))
            .Callback<QuickPickDefinition>(d => created.Add(d.Id))
            .Returns(Task.CompletedTask);

        await this._sut.SeedDefaultsAsync();

        var expected = (await this._sut.GetDefaultPicksAsync()).Select(d => d.Id).ToList();
        Assert.Equal(expected.Count, created.Count);
        Assert.Contains("all-invasions", created);
        Assert.Contains("invasion-leader", created);
    }

    [Fact]
    public async Task SeedingDefaultsClearsTheAppliedStateOfEveryPickItRemoves()
    {
        // Left behind, that state named a definition that no longer exists: never listed, never cleaned,
        // and the alarms it owned lost their Remove button for good. See #630.
        this._definitionRepository.Setup(r => r.GetAllGlobalAsync())
            .ReturnsAsync([
                new QuickPickDefinition { Id = "old-one", Scope = "global" },
                new QuickPickDefinition { Id = "old-two", Scope = "global" },
            ]);
        this._definitionRepository.Setup(r => r.GetByIdAsync(It.IsAny<string>())).ReturnsAsync((QuickPickDefinition?)null);

        await this._sut.SeedDefaultsAsync();

        this._appliedStateRepository.Verify(r => r.DeleteByQuickPickIdAsync("old-one", null), Times.Once);
        this._appliedStateRepository.Verify(r => r.DeleteByQuickPickIdAsync("old-two", null), Times.Once);
    }

    [Fact]
    public async Task SaveAdminPickGeneratesASlugWhenNoIdIsSupplied()
    {
        this._definitionRepository.Setup(r => r.GetByIdAsync(It.IsAny<string>())).ReturnsAsync((QuickPickDefinition?)null);

        var saved = await this._sut.SaveAdminPickAsync(new QuickPickDefinition { Id = "", Name = "Hundo IV!" });

        Assert.Equal("hundo-iv", saved.Id);
    }

    [Fact]
    public async Task SaveUserPickGeneratesASlugWhenNoIdIsSupplied()
    {
        this._definitionRepository.Setup(r => r.GetByIdAsync(It.IsAny<string>())).ReturnsAsync((QuickPickDefinition?)null);

        var saved = await this._sut.SaveUserPickAsync("owner", new QuickPickDefinition { Id = "  ", Name = "My  Pick" });

        Assert.Equal("my-pick", saved.Id);
        Assert.Equal("owner", saved.OwnerUserId);
    }

    [Fact]
    public async Task GeneratedIdsAvoidCollidingWithAnExistingPick()
    {
        this._definitionRepository.Setup(r => r.GetByIdAsync("hundo"))
            .ReturnsAsync(new QuickPickDefinition { Id = "hundo", Scope = "global" });
        this._definitionRepository.Setup(r => r.GetByIdAsync("hundo-2")).ReturnsAsync((QuickPickDefinition?)null);

        var saved = await this._sut.SaveAdminPickAsync(new QuickPickDefinition { Id = "", Name = "Hundo" });

        Assert.Equal("hundo-2", saved.Id);
    }

    [Fact]
    public async Task AnExplicitIdIsLeftAlone()
    {
        this._definitionRepository.Setup(r => r.GetByIdAsync("raid-5star")).ReturnsAsync((QuickPickDefinition?)null);

        var saved = await this._sut.SaveAdminPickAsync(new QuickPickDefinition { Id = "raid-5star", Name = "Whatever" });

        Assert.Equal("raid-5star", saved.Id);
    }

    [Fact]
    public async Task AnUnnamedPickStillGetsAUsableId()
    {
        this._definitionRepository.Setup(r => r.GetByIdAsync(It.IsAny<string>())).ReturnsAsync((QuickPickDefinition?)null);

        var saved = await this._sut.SaveAdminPickAsync(new QuickPickDefinition { Id = "", Name = "" });

        Assert.False(string.IsNullOrWhiteSpace(saved.Id));
    }

}
