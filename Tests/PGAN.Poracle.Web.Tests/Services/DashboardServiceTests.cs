using Moq;
using PGAN.Poracle.Web.Core.Abstractions.Repositories;
using PGAN.Poracle.Web.Core.Services;

namespace PGAN.Poracle.Web.Tests.Services;

public class DashboardServiceTests
{
    private readonly Mock<IMonsterRepository> _monsterRepo = new();
    private readonly Mock<IRaidRepository> _raidRepo = new();
    private readonly Mock<IEggRepository> _eggRepo = new();
    private readonly Mock<IQuestRepository> _questRepo = new();
    private readonly Mock<IInvasionRepository> _invasionRepo = new();
    private readonly Mock<ILureRepository> _lureRepo = new();
    private readonly Mock<INestRepository> _nestRepo = new();
    private readonly Mock<IGymRepository> _gymRepo = new();
    private readonly DashboardService _sut;

    public DashboardServiceTests() => this._sut = new DashboardService(
            this._monsterRepo.Object,
            this._raidRepo.Object,
            this._eggRepo.Object,
            this._questRepo.Object,
            this._invasionRepo.Object,
            this._lureRepo.Object,
            this._nestRepo.Object,
            this._gymRepo.Object);

    [Fact]
    public async Task GetCountsAsyncReturnsAllCounts()
    {
        this._monsterRepo.Setup(r => r.CountByUserAsync("u1", 1)).ReturnsAsync(10);
        this._raidRepo.Setup(r => r.CountByUserAsync("u1", 1)).ReturnsAsync(5);
        this._eggRepo.Setup(r => r.CountByUserAsync("u1", 1)).ReturnsAsync(3);
        this._questRepo.Setup(r => r.CountByUserAsync("u1", 1)).ReturnsAsync(7);
        this._invasionRepo.Setup(r => r.CountByUserAsync("u1", 1)).ReturnsAsync(2);
        this._lureRepo.Setup(r => r.CountByUserAsync("u1", 1)).ReturnsAsync(4);
        this._nestRepo.Setup(r => r.CountByUserAsync("u1", 1)).ReturnsAsync(1);
        this._gymRepo.Setup(r => r.CountByUserAsync("u1", 1)).ReturnsAsync(6);

        var result = await this._sut.GetCountsAsync("u1", 1);

        Assert.Equal(10, result.Monsters);
        Assert.Equal(5, result.Raids);
        Assert.Equal(3, result.Eggs);
        Assert.Equal(7, result.Quests);
        Assert.Equal(2, result.Invasions);
        Assert.Equal(4, result.Lures);
        Assert.Equal(1, result.Nests);
        Assert.Equal(6, result.Gyms);
    }

    [Fact]
    public async Task GetCountsAsyncReturnsZeroCountsWhenNoAlarms()
    {
        this._monsterRepo.Setup(r => r.CountByUserAsync("u1", 1)).ReturnsAsync(0);
        this._raidRepo.Setup(r => r.CountByUserAsync("u1", 1)).ReturnsAsync(0);
        this._eggRepo.Setup(r => r.CountByUserAsync("u1", 1)).ReturnsAsync(0);
        this._questRepo.Setup(r => r.CountByUserAsync("u1", 1)).ReturnsAsync(0);
        this._invasionRepo.Setup(r => r.CountByUserAsync("u1", 1)).ReturnsAsync(0);
        this._lureRepo.Setup(r => r.CountByUserAsync("u1", 1)).ReturnsAsync(0);
        this._nestRepo.Setup(r => r.CountByUserAsync("u1", 1)).ReturnsAsync(0);
        this._gymRepo.Setup(r => r.CountByUserAsync("u1", 1)).ReturnsAsync(0);

        var result = await this._sut.GetCountsAsync("u1", 1);

        Assert.Equal(0, result.Monsters);
        Assert.Equal(0, result.Raids);
    }

    [Fact]
    public async Task GetCountsAsyncCallsRepositoriesSequentially()
    {
        // Verify sequential execution (not parallel) to avoid DbContext concurrency issues
        var callOrder = new List<string>();

        this._monsterRepo.Setup(r => r.CountByUserAsync("u1", 1))
            .Callback(() => callOrder.Add("monsters")).ReturnsAsync(0);
        this._raidRepo.Setup(r => r.CountByUserAsync("u1", 1))
            .Callback(() => callOrder.Add("raids")).ReturnsAsync(0);
        this._eggRepo.Setup(r => r.CountByUserAsync("u1", 1))
            .Callback(() => callOrder.Add("eggs")).ReturnsAsync(0);
        this._questRepo.Setup(r => r.CountByUserAsync("u1", 1))
            .Callback(() => callOrder.Add("quests")).ReturnsAsync(0);
        this._invasionRepo.Setup(r => r.CountByUserAsync("u1", 1))
            .Callback(() => callOrder.Add("invasions")).ReturnsAsync(0);
        this._lureRepo.Setup(r => r.CountByUserAsync("u1", 1))
            .Callback(() => callOrder.Add("lures")).ReturnsAsync(0);
        this._nestRepo.Setup(r => r.CountByUserAsync("u1", 1))
            .Callback(() => callOrder.Add("nests")).ReturnsAsync(0);
        this._gymRepo.Setup(r => r.CountByUserAsync("u1", 1))
            .Callback(() => callOrder.Add("gyms")).ReturnsAsync(0);

        await this._sut.GetCountsAsync("u1", 1);

        Assert.Equal(8, callOrder.Count);
        Assert.Equal("monsters", callOrder[0]);
        Assert.Equal("gyms", callOrder[7]);
    }
}
