using Moq;
using PGAN.Poracle.Web.Core.Abstractions.Repositories;
using PGAN.Poracle.Web.Core.Models;
using PGAN.Poracle.Web.Core.Services;

namespace PGAN.Poracle.Web.Tests.Services;

public class RaidServiceTests
{
    private readonly Mock<IRaidRepository> _repository = new();
    private readonly RaidService _sut;

    public RaidServiceTests()
    {
        _sut = new RaidService(_repository.Object);
    }

    [Fact]
    public async Task GetByUserAsync_ReturnsRaids()
    {
        var raids = new List<Raid> { new() { Uid = 1, PokemonId = 150, Level = 5 } };
        _repository.Setup(r => r.GetByUserAsync("user1", 1)).ReturnsAsync(raids);

        var result = await _sut.GetByUserAsync("user1", 1);

        Assert.Single(result);
        Assert.Equal(5, result.First().Level);
    }

    [Fact]
    public async Task GetByUidAsync_ReturnsRaid()
    {
        var raid = new Raid { Uid = 1, PokemonId = 150 };
        _repository.Setup(r => r.GetByUidAsync(1)).ReturnsAsync(raid);

        var result = await _sut.GetByUidAsync(1);

        Assert.NotNull(result);
        Assert.Equal(150, result!.PokemonId);
    }

    [Fact]
    public async Task GetByUidAsync_ReturnsNull_WhenNotFound()
    {
        _repository.Setup(r => r.GetByUidAsync(999)).ReturnsAsync((Raid?)null);
        Assert.Null(await _sut.GetByUidAsync(999));
    }

    [Fact]
    public async Task CreateAsync_SetsUserId()
    {
        var raid = new Raid { PokemonId = 150 };
        _repository.Setup(r => r.CreateAsync(It.IsAny<Raid>())).ReturnsAsync((Raid r) => r);

        var result = await _sut.CreateAsync("user1", raid);

        Assert.Equal("user1", result.Id);
    }

    [Fact]
    public async Task UpdateAsync_CallsRepository()
    {
        var raid = new Raid { Uid = 1 };
        _repository.Setup(r => r.UpdateAsync(raid)).ReturnsAsync(raid);

        await _sut.UpdateAsync(raid);

        _repository.Verify(r => r.UpdateAsync(raid), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_ReturnsTrue() { _repository.Setup(r => r.DeleteAsync(1)).ReturnsAsync(true); Assert.True(await _sut.DeleteAsync(1)); }

    [Fact]
    public async Task DeleteAsync_ReturnsFalse() { _repository.Setup(r => r.DeleteAsync(999)).ReturnsAsync(false); Assert.False(await _sut.DeleteAsync(999)); }

    [Fact]
    public async Task DeleteAllByUserAsync_ReturnsCount() { _repository.Setup(r => r.DeleteAllByUserAsync("u", 1)).ReturnsAsync(3); Assert.Equal(3, await _sut.DeleteAllByUserAsync("u", 1)); }

    [Fact]
    public async Task UpdateDistanceByUserAsync_ReturnsCount() { _repository.Setup(r => r.UpdateDistanceByUserAsync("u", 1, 100)).ReturnsAsync(2); Assert.Equal(2, await _sut.UpdateDistanceByUserAsync("u", 1, 100)); }

    [Fact]
    public async Task CountByUserAsync_ReturnsCount() { _repository.Setup(r => r.CountByUserAsync("u", 1)).ReturnsAsync(7); Assert.Equal(7, await _sut.CountByUserAsync("u", 1)); }
}
