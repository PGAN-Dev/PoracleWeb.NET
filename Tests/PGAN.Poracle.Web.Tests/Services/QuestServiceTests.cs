using Moq;
using PGAN.Poracle.Web.Core.Abstractions.Repositories;
using PGAN.Poracle.Web.Core.Models;
using PGAN.Poracle.Web.Core.Services;

namespace PGAN.Poracle.Web.Tests.Services;

public class QuestServiceTests
{
    private readonly Mock<IQuestRepository> _repository = new();
    private readonly QuestService _sut;

    public QuestServiceTests() => _sut = new QuestService(_repository.Object);

    [Fact]
    public async Task GetByUserAsync_ReturnsQuests()
    {
        _repository.Setup(r => r.GetByUserAsync("u1", 1)).ReturnsAsync(new List<Quest> { new() { Uid = 1 } });
        Assert.Single(await _sut.GetByUserAsync("u1", 1));
    }

    [Fact]
    public async Task GetByUidAsync_Found() { _repository.Setup(r => r.GetByUidAsync(1)).ReturnsAsync(new Quest { Uid = 1 }); Assert.NotNull(await _sut.GetByUidAsync(1)); }

    [Fact]
    public async Task GetByUidAsync_NotFound() { _repository.Setup(r => r.GetByUidAsync(999)).ReturnsAsync((Quest?)null); Assert.Null(await _sut.GetByUidAsync(999)); }

    [Fact]
    public async Task CreateAsync_SetsUserId()
    {
        _repository.Setup(r => r.CreateAsync(It.IsAny<Quest>())).ReturnsAsync((Quest q) => q);
        var result = await _sut.CreateAsync("user1", new Quest());
        Assert.Equal("user1", result.Id);
    }

    [Fact]
    public async Task UpdateAsync_Delegates() { var q = new Quest { Uid = 1 }; _repository.Setup(r => r.UpdateAsync(q)).ReturnsAsync(q); await _sut.UpdateAsync(q); _repository.Verify(r => r.UpdateAsync(q), Times.Once); }

    [Fact]
    public async Task DeleteAsync_True() { _repository.Setup(r => r.DeleteAsync(1)).ReturnsAsync(true); Assert.True(await _sut.DeleteAsync(1)); }

    [Fact]
    public async Task DeleteAsync_False() { _repository.Setup(r => r.DeleteAsync(999)).ReturnsAsync(false); Assert.False(await _sut.DeleteAsync(999)); }

    [Fact]
    public async Task DeleteAllByUserAsync_Count() { _repository.Setup(r => r.DeleteAllByUserAsync("u", 1)).ReturnsAsync(2); Assert.Equal(2, await _sut.DeleteAllByUserAsync("u", 1)); }

    [Fact]
    public async Task UpdateDistanceByUserAsync_Count() { _repository.Setup(r => r.UpdateDistanceByUserAsync("u", 1, 100)).ReturnsAsync(1); Assert.Equal(1, await _sut.UpdateDistanceByUserAsync("u", 1, 100)); }

    [Fact]
    public async Task CountByUserAsync_Count() { _repository.Setup(r => r.CountByUserAsync("u", 1)).ReturnsAsync(8); Assert.Equal(8, await _sut.CountByUserAsync("u", 1)); }
}
