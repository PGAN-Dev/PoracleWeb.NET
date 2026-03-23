using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

public class RaidServiceTests
{
    private readonly Mock<IRaidRepository> _repository = new();
    private readonly RaidService _sut;

    public RaidServiceTests() => this._sut = new RaidService(this._repository.Object);

    [Fact]
    public async Task GetByUserAsyncReturnsRaids()
    {
        var raids = new List<Raid> { new() { Uid = 1, PokemonId = 150, Level = 5 } };
        this._repository.Setup(r => r.GetByUserAsync("user1", 1)).ReturnsAsync(raids);

        var result = await this._sut.GetByUserAsync("user1", 1);

        Assert.Single(result);
        Assert.Equal(5, result.First().Level);
    }

    [Fact]
    public async Task GetByUidAsyncReturnsRaid()
    {
        var raid = new Raid { Uid = 1, PokemonId = 150 };
        this._repository.Setup(r => r.GetByUidAsync(1)).ReturnsAsync(raid);

        var result = await this._sut.GetByUidAsync(1);

        Assert.NotNull(result);
        Assert.Equal(150, result!.PokemonId);
    }

    [Fact]
    public async Task GetByUidAsyncReturnsNullWhenNotFound()
    {
        this._repository.Setup(r => r.GetByUidAsync(999)).ReturnsAsync((Raid?)null);
        Assert.Null(await this._sut.GetByUidAsync(999));
    }

    [Fact]
    public async Task CreateAsyncSetsUserId()
    {
        var raid = new Raid { PokemonId = 150 };
        this._repository.Setup(r => r.CreateAsync(It.IsAny<Raid>())).ReturnsAsync((Raid r) => r);

        var result = await this._sut.CreateAsync("user1", raid);

        Assert.Equal("user1", result.Id);
    }

    [Fact]
    public async Task UpdateAsyncCallsRepository()
    {
        var raid = new Raid { Uid = 1 };
        this._repository.Setup(r => r.UpdateAsync(raid)).ReturnsAsync(raid);

        await this._sut.UpdateAsync(raid);

        this._repository.Verify(r => r.UpdateAsync(raid), Times.Once);
    }

    [Fact]
    public async Task DeleteAsyncReturnsTrue()
    {
        this._repository.Setup(r => r.DeleteAsync(1)).ReturnsAsync(true);
        Assert.True(await this._sut.DeleteAsync(1));
    }

    [Fact]
    public async Task DeleteAsyncReturnsFalse()
    {
        this._repository.Setup(r => r.DeleteAsync(999)).ReturnsAsync(false);
        Assert.False(await this._sut.DeleteAsync(999));
    }

    [Fact]
    public async Task DeleteAllByUserAsyncReturnsCount()
    {
        this._repository.Setup(r => r.DeleteAllByUserAsync("u", 1)).ReturnsAsync(3);
        Assert.Equal(3, await this._sut.DeleteAllByUserAsync("u", 1));
    }

    [Fact]
    public async Task UpdateDistanceByUserAsyncReturnsCount()
    {
        this._repository.Setup(r => r.UpdateDistanceByUserAsync("u", 1, 100)).ReturnsAsync(2);
        Assert.Equal(2, await this._sut.UpdateDistanceByUserAsync("u", 1, 100));
    }

    [Fact]
    public async Task CountByUserAsyncReturnsCount()
    {
        this._repository.Setup(r => r.CountByUserAsync("u", 1)).ReturnsAsync(7);
        Assert.Equal(7, await this._sut.CountByUserAsync("u", 1));
    }
}
