using Moq;
using PGAN.Poracle.Web.Core.Abstractions.Repositories;
using PGAN.Poracle.Web.Core.Abstractions.UnitsOfWork;
using PGAN.Poracle.Web.Core.Services;

namespace PGAN.Poracle.Web.Tests.Services;

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
        _unitOfWork.Setup(u => u.Monsters).Returns(_monsterRepo.Object);
        _unitOfWork.Setup(u => u.Raids).Returns(_raidRepo.Object);
        _unitOfWork.Setup(u => u.Eggs).Returns(_eggRepo.Object);
        _unitOfWork.Setup(u => u.Quests).Returns(_questRepo.Object);
        _unitOfWork.Setup(u => u.Invasions).Returns(_invasionRepo.Object);
        _unitOfWork.Setup(u => u.Lures).Returns(_lureRepo.Object);
        _unitOfWork.Setup(u => u.Nests).Returns(_nestRepo.Object);
        _unitOfWork.Setup(u => u.Gyms).Returns(_gymRepo.Object);
        _sut = new CleaningService(_unitOfWork.Object);
    }

    [Fact]
    public async Task ToggleCleanMonstersAsync_DelegatesToUnitOfWork()
    {
        _monsterRepo.Setup(r => r.BulkUpdateCleanAsync("u1", 1, 1)).ReturnsAsync(5);
        Assert.Equal(5, await _sut.ToggleCleanMonstersAsync("u1", 1, 1));
    }

    [Fact]
    public async Task ToggleCleanRaidsAsync_DelegatesToUnitOfWork()
    {
        _raidRepo.Setup(r => r.BulkUpdateCleanAsync("u1", 1, 0)).ReturnsAsync(3);
        Assert.Equal(3, await _sut.ToggleCleanRaidsAsync("u1", 1, 0));
    }

    [Fact]
    public async Task ToggleCleanEggsAsync_DelegatesToUnitOfWork()
    {
        _eggRepo.Setup(r => r.BulkUpdateCleanAsync("u1", 1, 1)).ReturnsAsync(2);
        Assert.Equal(2, await _sut.ToggleCleanEggsAsync("u1", 1, 1));
    }

    [Fact]
    public async Task ToggleCleanQuestsAsync_DelegatesToUnitOfWork()
    {
        _questRepo.Setup(r => r.BulkUpdateCleanAsync("u1", 1, 1)).ReturnsAsync(4);
        Assert.Equal(4, await _sut.ToggleCleanQuestsAsync("u1", 1, 1));
    }

    [Fact]
    public async Task ToggleCleanInvasionsAsync_DelegatesToUnitOfWork()
    {
        _invasionRepo.Setup(r => r.BulkUpdateCleanAsync("u1", 1, 0)).ReturnsAsync(6);
        Assert.Equal(6, await _sut.ToggleCleanInvasionsAsync("u1", 1, 0));
    }

    [Fact]
    public async Task ToggleCleanLuresAsync_DelegatesToUnitOfWork()
    {
        _lureRepo.Setup(r => r.BulkUpdateCleanAsync("u1", 1, 1)).ReturnsAsync(1);
        Assert.Equal(1, await _sut.ToggleCleanLuresAsync("u1", 1, 1));
    }

    [Fact]
    public async Task ToggleCleanNestsAsync_DelegatesToUnitOfWork()
    {
        _nestRepo.Setup(r => r.BulkUpdateCleanAsync("u1", 1, 1)).ReturnsAsync(8);
        Assert.Equal(8, await _sut.ToggleCleanNestsAsync("u1", 1, 1));
    }

    [Fact]
    public async Task ToggleCleanGymsAsync_DelegatesToUnitOfWork()
    {
        _gymRepo.Setup(r => r.BulkUpdateCleanAsync("u1", 1, 0)).ReturnsAsync(9);
        Assert.Equal(9, await _sut.ToggleCleanGymsAsync("u1", 1, 0));
    }
}
