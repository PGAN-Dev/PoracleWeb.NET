using Moq;
using PGAN.Poracle.Web.Core.Abstractions.Repositories;
using PGAN.Poracle.Web.Core.Models;
using PGAN.Poracle.Web.Core.Services;

namespace PGAN.Poracle.Web.Tests.Services;

public class QuestServiceTests
{
    private readonly Mock<IQuestRepository> _repository = new();
    private readonly QuestService _sut;

    public QuestServiceTests() => this._sut = new QuestService(this._repository.Object);

    [Fact]
    public async Task GetByUserAsyncReturnsQuests()
    {
        this._repository.Setup(r => r.GetByUserAsync("u1", 1)).ReturnsAsync(new List<Quest> { new() { Uid = 1 } });
        Assert.Single(await this._sut.GetByUserAsync("u1", 1));
    }

    [Fact]
    public async Task GetByUidAsyncFound()
    {
        this._repository.Setup(r => r.GetByUidAsync(1)).ReturnsAsync(new Quest { Uid = 1 });
        Assert.NotNull(await this._sut.GetByUidAsync(1));
    }

    [Fact]
    public async Task GetByUidAsyncNotFound()
    {
        this._repository.Setup(r => r.GetByUidAsync(999)).ReturnsAsync((Quest?)null);
        Assert.Null(await this._sut.GetByUidAsync(999));
    }

    [Fact]
    public async Task CreateAsyncSetsUserId()
    {
        this._repository.Setup(r => r.CreateAsync(It.IsAny<Quest>())).ReturnsAsync((Quest q) => q);
        var result = await this._sut.CreateAsync("user1", new Quest());
        Assert.Equal("user1", result.Id);
    }

    [Fact]
    public async Task UpdateAsyncDelegates()
    {
        var q = new Quest { Uid = 1 };
        this._repository.Setup(r => r.UpdateAsync(q)).ReturnsAsync(q);
        await this._sut.UpdateAsync(q);
        this._repository.Verify(r => r.UpdateAsync(q), Times.Once);
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
        this._repository.Setup(r => r.DeleteAllByUserAsync("u", 1)).ReturnsAsync(2);
        Assert.Equal(2, await this._sut.DeleteAllByUserAsync("u", 1));
    }

    [Fact]
    public async Task UpdateDistanceByUserAsyncCount()
    {
        this._repository.Setup(r => r.UpdateDistanceByUserAsync("u", 1, 100)).ReturnsAsync(1);
        Assert.Equal(1, await this._sut.UpdateDistanceByUserAsync("u", 1, 100));
    }

    [Fact]
    public async Task CountByUserAsyncCount()
    {
        this._repository.Setup(r => r.CountByUserAsync("u", 1)).ReturnsAsync(8);
        Assert.Equal(8, await this._sut.CountByUserAsync("u", 1));
    }
}
