using Moq;
using PGAN.Poracle.Web.Core.Abstractions.Repositories;
using PGAN.Poracle.Web.Core.Models;
using PGAN.Poracle.Web.Core.Services;

namespace PGAN.Poracle.Web.Tests.Services;

public class GymServiceTests
{
    private readonly Mock<IGymRepository> _repository = new();
    private readonly GymService _sut;

    public GymServiceTests() => this._sut = new GymService(this._repository.Object);

    [Fact]
    public async Task GetByUserAsyncReturnsGyms()
    {
        this._repository.Setup(r => r.GetByUserAsync("u1", 1)).ReturnsAsync(new List<Gym> { new() { Uid = 1 } });
        Assert.Single(await this._sut.GetByUserAsync("u1", 1));
    }

    [Fact]
    public async Task GetByUidAsyncFound()
    {
        this._repository.Setup(r => r.GetByUidAsync(1)).ReturnsAsync(new Gym { Uid = 1 });
        Assert.NotNull(await this._sut.GetByUidAsync(1));
    }

    [Fact]
    public async Task GetByUidAsyncNotFound()
    {
        this._repository.Setup(r => r.GetByUidAsync(999)).ReturnsAsync((Gym?)null);
        Assert.Null(await this._sut.GetByUidAsync(999));
    }

    [Fact]
    public async Task CreateAsyncSetsUserId()
    {
        this._repository.Setup(r => r.CreateAsync(It.IsAny<Gym>())).ReturnsAsync((Gym g) => g);
        Assert.Equal("user1", (await this._sut.CreateAsync("user1", new Gym())).Id);
    }

    [Fact]
    public async Task DeleteAsyncTrue()
    {
        this._repository.Setup(r => r.DeleteAsync(1)).ReturnsAsync(true);
        Assert.True(await this._sut.DeleteAsync(1));
    }

    [Fact]
    public async Task DeleteAsyncFalse()
    {
        this._repository.Setup(r => r.DeleteAsync(999)).ReturnsAsync(false);
        Assert.False(await this._sut.DeleteAsync(999));
    }

    [Fact]
    public async Task DeleteAllByUserAsyncCount()
    {
        this._repository.Setup(r => r.DeleteAllByUserAsync("u", 1)).ReturnsAsync(7);
        Assert.Equal(7, await this._sut.DeleteAllByUserAsync("u", 1));
    }

    [Fact]
    public async Task UpdateDistanceByUserAsyncCount()
    {
        this._repository.Setup(r => r.UpdateDistanceByUserAsync("u", 1, 250)).ReturnsAsync(5);
        Assert.Equal(5, await this._sut.UpdateDistanceByUserAsync("u", 1, 250));
    }

    [Fact]
    public async Task CountByUserAsyncCount()
    {
        this._repository.Setup(r => r.CountByUserAsync("u", 1)).ReturnsAsync(11);
        Assert.Equal(11, await this._sut.CountByUserAsync("u", 1));
    }
}
