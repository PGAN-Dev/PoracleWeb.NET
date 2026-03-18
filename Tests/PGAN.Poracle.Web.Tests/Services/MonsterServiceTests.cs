using Moq;
using PGAN.Poracle.Web.Core.Abstractions.Repositories;
using PGAN.Poracle.Web.Core.Models;
using PGAN.Poracle.Web.Core.Services;

namespace PGAN.Poracle.Web.Tests.Services;

public class MonsterServiceTests
{
    private readonly Mock<IMonsterRepository> _repository = new();
    private readonly MonsterService _sut;

    public MonsterServiceTests()
    {
        _sut = new MonsterService(_repository.Object);
    }

    [Fact]
    public async Task GetByUserAsync_ReturnsMonsters()
    {
        var monsters = new List<Monster> { new() { Uid = 1, PokemonId = 25 } };
        _repository.Setup(r => r.GetByUserAsync("user1", 1)).ReturnsAsync(monsters);

        var result = await _sut.GetByUserAsync("user1", 1);

        Assert.Single(result);
        Assert.Equal(25, result.First().PokemonId);
    }

    [Fact]
    public async Task GetByUidAsync_ReturnsMonster()
    {
        var monster = new Monster { Uid = 1, PokemonId = 25 };
        _repository.Setup(r => r.GetByUidAsync(1)).ReturnsAsync(monster);

        var result = await _sut.GetByUidAsync(1);

        Assert.NotNull(result);
        Assert.Equal(25, result!.PokemonId);
    }

    [Fact]
    public async Task GetByUidAsync_ReturnsNull_WhenNotFound()
    {
        _repository.Setup(r => r.GetByUidAsync(999)).ReturnsAsync((Monster?)null);

        var result = await _sut.GetByUidAsync(999);

        Assert.Null(result);
    }

    [Fact]
    public async Task CreateAsync_SetsUserIdAndDefaults()
    {
        var monster = new Monster { PokemonId = 25, Ping = null, Template = null };
        _repository.Setup(r => r.CreateAsync(It.IsAny<Monster>()))
            .ReturnsAsync((Monster m) => m);

        var result = await _sut.CreateAsync("user1", monster);

        Assert.Equal("user1", result.Id);
        Assert.Equal(string.Empty, result.Ping);
        Assert.Equal(string.Empty, result.Template);
    }

    [Fact]
    public async Task CreateAsync_PreservesNonNullPingAndTemplate()
    {
        var monster = new Monster { PokemonId = 25, Ping = "<@123>", Template = "custom" };
        _repository.Setup(r => r.CreateAsync(It.IsAny<Monster>()))
            .ReturnsAsync((Monster m) => m);

        var result = await _sut.CreateAsync("user1", monster);

        Assert.Equal("<@123>", result.Ping);
        Assert.Equal("custom", result.Template);
    }

    [Fact]
    public async Task UpdateAsync_CallsRepository()
    {
        var monster = new Monster { Uid = 1, PokemonId = 25 };
        _repository.Setup(r => r.UpdateAsync(monster)).ReturnsAsync(monster);

        var result = await _sut.UpdateAsync(monster);

        Assert.Equal(1, result.Uid);
        _repository.Verify(r => r.UpdateAsync(monster), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_ReturnsTrue_WhenDeleted()
    {
        _repository.Setup(r => r.DeleteAsync(1)).ReturnsAsync(true);

        var result = await _sut.DeleteAsync(1);

        Assert.True(result);
    }

    [Fact]
    public async Task DeleteAsync_ReturnsFalse_WhenNotFound()
    {
        _repository.Setup(r => r.DeleteAsync(999)).ReturnsAsync(false);

        var result = await _sut.DeleteAsync(999);

        Assert.False(result);
    }

    [Fact]
    public async Task DeleteAllByUserAsync_ReturnsCount()
    {
        _repository.Setup(r => r.DeleteAllByUserAsync("user1", 1)).ReturnsAsync(5);

        var result = await _sut.DeleteAllByUserAsync("user1", 1);

        Assert.Equal(5, result);
    }

    [Fact]
    public async Task UpdateDistanceByUserAsync_ReturnsCount()
    {
        _repository.Setup(r => r.UpdateDistanceByUserAsync("user1", 1, 500)).ReturnsAsync(3);

        var result = await _sut.UpdateDistanceByUserAsync("user1", 1, 500);

        Assert.Equal(3, result);
    }

    [Fact]
    public async Task CountByUserAsync_ReturnsCount()
    {
        _repository.Setup(r => r.CountByUserAsync("user1", 1)).ReturnsAsync(10);

        var result = await _sut.CountByUserAsync("user1", 1);

        Assert.Equal(10, result);
    }
}
