using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

public class MonsterServiceTests
{
    private readonly Mock<IMonsterRepository> _repository = new();
    private readonly MonsterService _sut;

    public MonsterServiceTests() => this._sut = new MonsterService(this._repository.Object);

    [Fact]
    public async Task GetByUserAsyncReturnsMonsters()
    {
        var monsters = new List<Monster> { new() { Uid = 1, PokemonId = 25 } };
        this._repository.Setup(r => r.GetByUserAsync("user1", 1)).ReturnsAsync(monsters);

        var result = await this._sut.GetByUserAsync("user1", 1);

        Assert.Single(result);
        Assert.Equal(25, result.First().PokemonId);
    }

    [Fact]
    public async Task GetByUidAsyncReturnsMonster()
    {
        var monster = new Monster { Uid = 1, PokemonId = 25 };
        this._repository.Setup(r => r.GetByUidAsync(1)).ReturnsAsync(monster);

        var result = await this._sut.GetByUidAsync(1);

        Assert.NotNull(result);
        Assert.Equal(25, result!.PokemonId);
    }

    [Fact]
    public async Task GetByUidAsyncReturnsNullWhenNotFound()
    {
        this._repository.Setup(r => r.GetByUidAsync(999)).ReturnsAsync((Monster?)null);

        var result = await this._sut.GetByUidAsync(999);

        Assert.Null(result);
    }

    [Fact]
    public async Task CreateAsyncSetsUserIdAndDefaults()
    {
        var monster = new Monster { PokemonId = 25, Ping = null, Template = null };
        this._repository.Setup(r => r.CreateAsync(It.IsAny<Monster>()))
            .ReturnsAsync((Monster m) => m);

        var result = await this._sut.CreateAsync("user1", monster);

        Assert.Equal("user1", result.Id);
        Assert.Equal(string.Empty, result.Ping);
        Assert.Equal(string.Empty, result.Template);
    }

    [Fact]
    public async Task CreateAsyncPreservesNonNullPingAndTemplate()
    {
        var monster = new Monster { PokemonId = 25, Ping = "<@123>", Template = "custom" };
        this._repository.Setup(r => r.CreateAsync(It.IsAny<Monster>()))
            .ReturnsAsync((Monster m) => m);

        var result = await this._sut.CreateAsync("user1", monster);

        Assert.Equal("<@123>", result.Ping);
        Assert.Equal("custom", result.Template);
    }

    [Fact]
    public async Task UpdateAsyncCallsRepository()
    {
        var monster = new Monster { Uid = 1, PokemonId = 25 };
        this._repository.Setup(r => r.UpdateAsync(monster)).ReturnsAsync(monster);

        var result = await this._sut.UpdateAsync(monster);

        Assert.Equal(1, result.Uid);
        this._repository.Verify(r => r.UpdateAsync(monster), Times.Once);
    }

    [Fact]
    public async Task DeleteAsyncReturnsTrueWhenDeleted()
    {
        this._repository.Setup(r => r.DeleteAsync(1)).ReturnsAsync(true);

        var result = await this._sut.DeleteAsync(1);

        Assert.True(result);
    }

    [Fact]
    public async Task DeleteAsyncReturnsFalseWhenNotFound()
    {
        this._repository.Setup(r => r.DeleteAsync(999)).ReturnsAsync(false);

        var result = await this._sut.DeleteAsync(999);

        Assert.False(result);
    }

    [Fact]
    public async Task DeleteAllByUserAsyncReturnsCount()
    {
        this._repository.Setup(r => r.DeleteAllByUserAsync("user1", 1)).ReturnsAsync(5);

        var result = await this._sut.DeleteAllByUserAsync("user1", 1);

        Assert.Equal(5, result);
    }

    [Fact]
    public async Task UpdateDistanceByUserAsyncReturnsCount()
    {
        this._repository.Setup(r => r.UpdateDistanceByUserAsync("user1", 1, 500)).ReturnsAsync(3);

        var result = await this._sut.UpdateDistanceByUserAsync("user1", 1, 500);

        Assert.Equal(3, result);
    }

    [Fact]
    public async Task CountByUserAsyncReturnsCount()
    {
        this._repository.Setup(r => r.CountByUserAsync("user1", 1)).ReturnsAsync(10);

        var result = await this._sut.CountByUserAsync("user1", 1);

        Assert.Equal(10, result);
    }

    [Fact]
    public async Task CreateAsyncResetsPvpFieldsWhenNoLeague()
    {
        var monster = new Monster { PokemonId = 25, PvpRankingLeague = 0, PvpRankingBest = 1, PvpRankingWorst = 100, PvpRankingMinCp = 500 };
        this._repository.Setup(r => r.CreateAsync(It.IsAny<Monster>()))
            .ReturnsAsync((Monster m) => m);

        var result = await this._sut.CreateAsync("user1", monster);

        Assert.Equal(0, result.PvpRankingBest);
        Assert.Equal(4096, result.PvpRankingWorst);
        Assert.Equal(0, result.PvpRankingMinCp);
    }

    [Fact]
    public async Task CreateAsyncPreservesPvpFieldsWhenLeagueSet()
    {
        var monster = new Monster { PokemonId = 25, PvpRankingLeague = 1500, PvpRankingBest = 1, PvpRankingWorst = 100, PvpRankingMinCp = 500 };
        this._repository.Setup(r => r.CreateAsync(It.IsAny<Monster>()))
            .ReturnsAsync((Monster m) => m);

        var result = await this._sut.CreateAsync("user1", monster);

        Assert.Equal(1, result.PvpRankingBest);
        Assert.Equal(100, result.PvpRankingWorst);
        Assert.Equal(500, result.PvpRankingMinCp);
    }

    [Fact]
    public async Task UpdateAsyncResetsPvpFieldsWhenNoLeague()
    {
        var monster = new Monster { Uid = 1, PokemonId = 25, PvpRankingLeague = 0, PvpRankingBest = 1, PvpRankingWorst = 100 };
        this._repository.Setup(r => r.UpdateAsync(It.IsAny<Monster>()))
            .ReturnsAsync((Monster m) => m);

        var result = await this._sut.UpdateAsync(monster);

        Assert.Equal(0, result.PvpRankingBest);
        Assert.Equal(4096, result.PvpRankingWorst);
    }

    [Fact]
    public async Task BulkCreateAsyncResetsPvpFieldsWhenNoLeague()
    {
        var monsters = new List<Monster>
        {
            new() { PokemonId = 25, PvpRankingLeague = 0, PvpRankingBest = 1, PvpRankingWorst = 100 },
            new() { PokemonId = 150, PvpRankingLeague = 1500, PvpRankingBest = 1, PvpRankingWorst = 50 },
        };
        this._repository.Setup(r => r.BulkCreateAsync(It.IsAny<IEnumerable<Monster>>()))
            .ReturnsAsync((IEnumerable<Monster> m) => m.ToList());

        var result = (await this._sut.BulkCreateAsync("user1", monsters)).ToList();

        Assert.Equal(4096, result[0].PvpRankingWorst);
        Assert.Equal(0, result[0].PvpRankingBest);
        Assert.Equal(50, result[1].PvpRankingWorst);
        Assert.Equal(1, result[1].PvpRankingBest);
    }
}
