using Moq;
using PGAN.Poracle.Web.Core.Abstractions.Repositories;
using PGAN.Poracle.Web.Core.Models;
using PGAN.Poracle.Web.Core.Services;

namespace PGAN.Poracle.Web.Tests.Services;

public class EggServiceTests
{
    private readonly Mock<IEggRepository> _repository = new();
    private readonly EggService _sut;

    public EggServiceTests() => _sut = new EggService(_repository.Object);

    [Fact]
    public async Task GetByUserAsync_ReturnsEggs()
    {
        _repository.Setup(r => r.GetByUserAsync("u1", 1)).ReturnsAsync(new List<Egg> { new() { Uid = 1 } });
        var result = await _sut.GetByUserAsync("u1", 1);
        Assert.Single(result);
    }

    [Fact]
    public async Task GetByUidAsync_ReturnsEgg()
    {
        _repository.Setup(r => r.GetByUidAsync(1)).ReturnsAsync(new Egg { Uid = 1 });
        Assert.NotNull(await _sut.GetByUidAsync(1));
    }

    [Fact]
    public async Task GetByUidAsync_ReturnsNull_WhenNotFound()
    {
        _repository.Setup(r => r.GetByUidAsync(999)).ReturnsAsync((Egg?)null);
        Assert.Null(await _sut.GetByUidAsync(999));
    }

    [Fact]
    public async Task CreateAsync_SetsUserId()
    {
        var egg = new Egg();
        _repository.Setup(r => r.CreateAsync(It.IsAny<Egg>())).ReturnsAsync((Egg e) => e);
        var result = await _sut.CreateAsync("user1", egg);
        Assert.Equal("user1", result.Id);
    }

    [Fact]
    public async Task UpdateAsync_CallsRepository()
    {
        var egg = new Egg { Uid = 1 };
        _repository.Setup(r => r.UpdateAsync(egg)).ReturnsAsync(egg);
        await _sut.UpdateAsync(egg);
        _repository.Verify(r => r.UpdateAsync(egg), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_ReturnsExpectedResult()
    {
        _repository.Setup(r => r.DeleteAsync(1)).ReturnsAsync(true);
        Assert.True(await _sut.DeleteAsync(1));
        _repository.Setup(r => r.DeleteAsync(999)).ReturnsAsync(false);
        Assert.False(await _sut.DeleteAsync(999));
    }

    [Fact]
    public async Task DeleteAllByUserAsync_ReturnsCount()
    {
        _repository.Setup(r => r.DeleteAllByUserAsync("u", 1)).ReturnsAsync(4);
        Assert.Equal(4, await _sut.DeleteAllByUserAsync("u", 1));
    }

    [Fact]
    public async Task UpdateDistanceByUserAsync_ReturnsCount()
    {
        _repository.Setup(r => r.UpdateDistanceByUserAsync("u", 1, 200)).ReturnsAsync(3);
        Assert.Equal(3, await _sut.UpdateDistanceByUserAsync("u", 1, 200));
    }

    [Fact]
    public async Task CountByUserAsync_ReturnsCount()
    {
        _repository.Setup(r => r.CountByUserAsync("u", 1)).ReturnsAsync(5);
        Assert.Equal(5, await _sut.CountByUserAsync("u", 1));
    }
}
