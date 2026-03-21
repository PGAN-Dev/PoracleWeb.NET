using Moq;
using PGAN.Poracle.Web.Core.Abstractions.Repositories;
using PGAN.Poracle.Web.Core.Models;
using PGAN.Poracle.Web.Core.Services;

namespace PGAN.Poracle.Web.Tests.Services;

public class NestServiceTests
{
    private readonly Mock<INestRepository> _repository = new();
    private readonly NestService _sut;

    public NestServiceTests() => this._sut = new NestService(this._repository.Object);

    [Fact]
    public async Task GetByUserAsyncReturnsNests()
    {
        this._repository.Setup(r => r.GetByUserAsync("u1", 1)).ReturnsAsync([new() { Uid = 1 }]);
        Assert.Single(await this._sut.GetByUserAsync("u1", 1));
    }

    [Fact]
    public async Task GetByUidAsyncFound()
    {
        this._repository.Setup(r => r.GetByUidAsync(1)).ReturnsAsync(new Nest { Uid = 1 });
        Assert.NotNull(await this._sut.GetByUidAsync(1));
    }

    [Fact]
    public async Task GetByUidAsyncNotFound()
    {
        this._repository.Setup(r => r.GetByUidAsync(999)).ReturnsAsync((Nest?)null);
        Assert.Null(await this._sut.GetByUidAsync(999));
    }

    [Fact]
    public async Task CreateAsyncSetsUserId()
    {
        this._repository.Setup(r => r.CreateAsync(It.IsAny<Nest>())).ReturnsAsync((Nest n) => n);
        Assert.Equal("user1", (await this._sut.CreateAsync("user1", new Nest())).Id);
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
        this._repository.Setup(r => r.DeleteAllByUserAsync("u", 1)).ReturnsAsync(5);
        Assert.Equal(5, await this._sut.DeleteAllByUserAsync("u", 1));
    }

    [Fact]
    public async Task UpdateDistanceByUserAsyncCount()
    {
        this._repository.Setup(r => r.UpdateDistanceByUserAsync("u", 1, 100)).ReturnsAsync(3);
        Assert.Equal(3, await this._sut.UpdateDistanceByUserAsync("u", 1, 100));
    }

    [Fact]
    public async Task CountByUserAsyncCount()
    {
        this._repository.Setup(r => r.CountByUserAsync("u", 1)).ReturnsAsync(9);
        Assert.Equal(9, await this._sut.CountByUserAsync("u", 1));
    }
}
