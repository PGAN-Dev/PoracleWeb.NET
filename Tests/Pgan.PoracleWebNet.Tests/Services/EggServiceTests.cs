using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

public class EggServiceTests
{
    private readonly Mock<IEggRepository> _repository = new();
    private readonly EggService _sut;

    public EggServiceTests() => this._sut = new EggService(this._repository.Object);

    [Fact]
    public async Task GetByUserAsyncReturnsEggs()
    {
        this._repository.Setup(r => r.GetByUserAsync("u1", 1)).ReturnsAsync([new() { Uid = 1 }]);
        var result = await this._sut.GetByUserAsync("u1", 1);
        Assert.Single(result);
    }

    [Fact]
    public async Task GetByUidAsyncReturnsEgg()
    {
        this._repository.Setup(r => r.GetByUidAsync(1)).ReturnsAsync(new Egg { Uid = 1 });
        Assert.NotNull(await this._sut.GetByUidAsync(1));
    }

    [Fact]
    public async Task GetByUidAsyncReturnsNullWhenNotFound()
    {
        this._repository.Setup(r => r.GetByUidAsync(999)).ReturnsAsync((Egg?)null);
        Assert.Null(await this._sut.GetByUidAsync(999));
    }

    [Fact]
    public async Task CreateAsyncSetsUserId()
    {
        var egg = new Egg();
        this._repository.Setup(r => r.CreateAsync(It.IsAny<Egg>())).ReturnsAsync((Egg e) => e);
        var result = await this._sut.CreateAsync("user1", egg);
        Assert.Equal("user1", result.Id);
    }

    [Fact]
    public async Task UpdateAsyncCallsRepository()
    {
        var egg = new Egg { Uid = 1 };
        this._repository.Setup(r => r.UpdateAsync(egg)).ReturnsAsync(egg);
        await this._sut.UpdateAsync(egg);
        this._repository.Verify(r => r.UpdateAsync(egg), Times.Once);
    }

    [Fact]
    public async Task DeleteAsyncReturnsExpectedResult()
    {
        this._repository.Setup(r => r.DeleteAsync(1)).ReturnsAsync(true);
        Assert.True(await this._sut.DeleteAsync(1));
        this._repository.Setup(r => r.DeleteAsync(999)).ReturnsAsync(false);
        Assert.False(await this._sut.DeleteAsync(999));
    }

    [Fact]
    public async Task DeleteAllByUserAsyncReturnsCount()
    {
        this._repository.Setup(r => r.DeleteAllByUserAsync("u", 1)).ReturnsAsync(4);
        Assert.Equal(4, await this._sut.DeleteAllByUserAsync("u", 1));
    }

    [Fact]
    public async Task UpdateDistanceByUserAsyncReturnsCount()
    {
        this._repository.Setup(r => r.UpdateDistanceByUserAsync("u", 1, 200)).ReturnsAsync(3);
        Assert.Equal(3, await this._sut.UpdateDistanceByUserAsync("u", 1, 200));
    }

    [Fact]
    public async Task CountByUserAsyncReturnsCount()
    {
        this._repository.Setup(r => r.CountByUserAsync("u", 1)).ReturnsAsync(5);
        Assert.Equal(5, await this._sut.CountByUserAsync("u", 1));
    }
}
