using Moq;
using PGAN.Poracle.Web.Core.Abstractions.Repositories;
using PGAN.Poracle.Web.Core.Models;
using PGAN.Poracle.Web.Core.Services;

namespace PGAN.Poracle.Web.Tests.Services;

public class LureServiceTests
{
    private readonly Mock<ILureRepository> _repository = new();
    private readonly LureService _sut;

    public LureServiceTests() => _sut = new LureService(_repository.Object);

    [Fact]
    public async Task GetByUserAsync_ReturnsLures()
    {
        _repository.Setup(r => r.GetByUserAsync("u1", 1)).ReturnsAsync(new List<Lure> { new() { Uid = 1 } });
        Assert.Single(await _sut.GetByUserAsync("u1", 1));
    }

    [Fact]
    public async Task GetByUidAsync_Found() { _repository.Setup(r => r.GetByUidAsync(1)).ReturnsAsync(new Lure { Uid = 1 }); Assert.NotNull(await _sut.GetByUidAsync(1)); }

    [Fact]
    public async Task GetByUidAsync_NotFound() { _repository.Setup(r => r.GetByUidAsync(999)).ReturnsAsync((Lure?)null); Assert.Null(await _sut.GetByUidAsync(999)); }

    [Fact]
    public async Task CreateAsync_SetsUserId()
    {
        _repository.Setup(r => r.CreateAsync(It.IsAny<Lure>())).ReturnsAsync((Lure l) => l);
        Assert.Equal("user1", (await _sut.CreateAsync("user1", new Lure())).Id);
    }

    [Fact]
    public async Task DeleteAsync_True() { _repository.Setup(r => r.DeleteAsync(1)).ReturnsAsync(true); Assert.True(await _sut.DeleteAsync(1)); }

    [Fact]
    public async Task DeleteAsync_False() { _repository.Setup(r => r.DeleteAsync(999)).ReturnsAsync(false); Assert.False(await _sut.DeleteAsync(999)); }

    [Fact]
    public async Task DeleteAllByUserAsync_Count() { _repository.Setup(r => r.DeleteAllByUserAsync("u", 1)).ReturnsAsync(3); Assert.Equal(3, await _sut.DeleteAllByUserAsync("u", 1)); }

    [Fact]
    public async Task UpdateDistanceByUserAsync_Count() { _repository.Setup(r => r.UpdateDistanceByUserAsync("u", 1, 300)).ReturnsAsync(2); Assert.Equal(2, await _sut.UpdateDistanceByUserAsync("u", 1, 300)); }

    [Fact]
    public async Task CountByUserAsync_Count() { _repository.Setup(r => r.CountByUserAsync("u", 1)).ReturnsAsync(4); Assert.Equal(4, await _sut.CountByUserAsync("u", 1)); }
}
