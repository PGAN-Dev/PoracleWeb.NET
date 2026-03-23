using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Abstractions.UnitsOfWork;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

public class CleaningServiceTests
{
    private readonly Mock<IPoracleUnitOfWork> _unitOfWork = new();
    private readonly Mock<IMonsterRepository> _monsterRepo = new();
    private readonly Mock<IRaidRepository> _raidRepo = new();
    private readonly Mock<IEggRepository> _eggRepo = new();
    private readonly Mock<IQuestRepository> _questRepo = new();
    private readonly Mock<IInvasionRepository> _invasionRepo = new();
    private readonly Mock<ILureRepository> _lureRepo = new();
    private readonly Mock<INestRepository> _nestRepo = new();
    private readonly Mock<IGymRepository> _gymRepo = new();
    private readonly CleaningService _sut;

    public CleaningServiceTests()
    {
        this._unitOfWork.Setup(u => u.Monsters).Returns(this._monsterRepo.Object);
        this._unitOfWork.Setup(u => u.Raids).Returns(this._raidRepo.Object);
        this._unitOfWork.Setup(u => u.Eggs).Returns(this._eggRepo.Object);
        this._unitOfWork.Setup(u => u.Quests).Returns(this._questRepo.Object);
        this._unitOfWork.Setup(u => u.Invasions).Returns(this._invasionRepo.Object);
        this._unitOfWork.Setup(u => u.Lures).Returns(this._lureRepo.Object);
        this._unitOfWork.Setup(u => u.Nests).Returns(this._nestRepo.Object);
        this._unitOfWork.Setup(u => u.Gyms).Returns(this._gymRepo.Object);
        this._sut = new CleaningService(this._unitOfWork.Object);
    }

    [Fact]
    public async Task ToggleCleanMonstersAsyncDelegatesToUnitOfWork()
    {
        this._monsterRepo.Setup(r => r.BulkUpdateCleanAsync("u1", 1, 1)).ReturnsAsync(5);
        Assert.Equal(5, await this._sut.ToggleCleanMonstersAsync("u1", 1, 1));
    }

    [Fact]
    public async Task ToggleCleanRaidsAsyncDelegatesToUnitOfWork()
    {
        this._raidRepo.Setup(r => r.BulkUpdateCleanAsync("u1", 1, 0)).ReturnsAsync(3);
        Assert.Equal(3, await this._sut.ToggleCleanRaidsAsync("u1", 1, 0));
    }

    [Fact]
    public async Task ToggleCleanEggsAsyncDelegatesToUnitOfWork()
    {
        this._eggRepo.Setup(r => r.BulkUpdateCleanAsync("u1", 1, 1)).ReturnsAsync(2);
        Assert.Equal(2, await this._sut.ToggleCleanEggsAsync("u1", 1, 1));
    }

    [Fact]
    public async Task ToggleCleanQuestsAsyncDelegatesToUnitOfWork()
    {
        this._questRepo.Setup(r => r.BulkUpdateCleanAsync("u1", 1, 1)).ReturnsAsync(4);
        Assert.Equal(4, await this._sut.ToggleCleanQuestsAsync("u1", 1, 1));
    }

    [Fact]
    public async Task ToggleCleanInvasionsAsyncDelegatesToUnitOfWork()
    {
        this._invasionRepo.Setup(r => r.BulkUpdateCleanAsync("u1", 1, 0)).ReturnsAsync(6);
        Assert.Equal(6, await this._sut.ToggleCleanInvasionsAsync("u1", 1, 0));
    }

    [Fact]
    public async Task ToggleCleanLuresAsyncDelegatesToUnitOfWork()
    {
        this._lureRepo.Setup(r => r.BulkUpdateCleanAsync("u1", 1, 1)).ReturnsAsync(1);
        Assert.Equal(1, await this._sut.ToggleCleanLuresAsync("u1", 1, 1));
    }

    [Fact]
    public async Task ToggleCleanNestsAsyncDelegatesToUnitOfWork()
    {
        this._nestRepo.Setup(r => r.BulkUpdateCleanAsync("u1", 1, 1)).ReturnsAsync(8);
        Assert.Equal(8, await this._sut.ToggleCleanNestsAsync("u1", 1, 1));
    }

    [Fact]
    public async Task ToggleCleanGymsAsyncDelegatesToUnitOfWork()
    {
        this._gymRepo.Setup(r => r.BulkUpdateCleanAsync("u1", 1, 0)).ReturnsAsync(9);
        Assert.Equal(9, await this._sut.ToggleCleanGymsAsync("u1", 1, 0));
    }
}
