using Moq;
using PGAN.Poracle.Web.Core.Abstractions.Repositories;
using PGAN.Poracle.Web.Core.Models;
using PGAN.Poracle.Web.Core.Services;

namespace PGAN.Poracle.Web.Tests.Services;

public class InvasionServiceTests
{
    private readonly Mock<IInvasionRepository> _repository = new();
    private readonly InvasionService _sut;

    public InvasionServiceTests() => _sut = new InvasionService(_repository.Object);

    [Fact]
    public async Task GetByUserAsync_ReturnsInvasions()
    {
        _repository.Setup(r => r.GetByUserAsync("u1", 1)).ReturnsAsync(new List<Invasion> { new() { Uid = 1 } });
        Assert.Single(await _sut.GetByUserAsync("u1", 1));
    }

    [Fact]
    public async Task GetByUidAsync_Found() { _repository.Setup(r => r.GetByUidAsync(1)).ReturnsAsync(new Invasion { Uid = 1 }); Assert.NotNull(await _sut.GetByUidAsync(1)); }

    [Fact]
    public async Task GetByUidAsync_NotFound() { _repository.Setup(r => r.GetByUidAsync(999)).ReturnsAsync((Invasion?)null); Assert.Null(await _sut.GetByUidAsync(999)); }

    [Fact]
    public async Task CreateAsync_SetsUserId()
    {
        _repository.Setup(r => r.CreateAsync(It.IsAny<Invasion>())).ReturnsAsync((Invasion i) => i);
        var result = await _sut.CreateAsync("user1", new Invasion());
        Assert.Equal("user1", result.Id);
    }

    [Fact]
    public async Task UpdateAsync_Delegates() { var i = new Invasion { Uid = 1 }; _repository.Setup(r => r.UpdateAsync(i)).ReturnsAsync(i); await _sut.UpdateAsync(i); _repository.Verify(r => r.UpdateAsync(i), Times.Once); }

    [Fact]
    public async Task DeleteAsync_True() { _repository.Setup(r => r.DeleteAsync(1)).ReturnsAsync(true); Assert.True(await _sut.DeleteAsync(1)); }

    [Fact]
    public async Task DeleteAsync_False() { _repository.Setup(r => r.DeleteAsync(999)).ReturnsAsync(false); Assert.False(await _sut.DeleteAsync(999)); }

    [Fact]
    public async Task DeleteAllByUserAsync_Count() { _repository.Setup(r => r.DeleteAllByUserAsync("u", 1)).ReturnsAsync(6); Assert.Equal(6, await _sut.DeleteAllByUserAsync("u", 1)); }

    [Fact]
    public async Task UpdateDistanceByUserAsync_Count() { _repository.Setup(r => r.UpdateDistanceByUserAsync("u", 1, 50)).ReturnsAsync(4); Assert.Equal(4, await _sut.UpdateDistanceByUserAsync("u", 1, 50)); }

    [Fact]
    public async Task CountByUserAsync_Count() { _repository.Setup(r => r.CountByUserAsync("u", 1)).ReturnsAsync(12); Assert.Equal(12, await _sut.CountByUserAsync("u", 1)); }
}
