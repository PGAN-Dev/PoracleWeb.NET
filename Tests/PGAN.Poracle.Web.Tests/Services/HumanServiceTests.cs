using Moq;
using PGAN.Poracle.Web.Core.Abstractions.Repositories;
using PGAN.Poracle.Web.Core.Models;
using PGAN.Poracle.Web.Core.Services;

namespace PGAN.Poracle.Web.Tests.Services;

public class HumanServiceTests
{
    private readonly Mock<IHumanRepository> _repository = new();
    private readonly HumanService _sut;

    public HumanServiceTests() => _sut = new HumanService(_repository.Object);

    [Fact]
    public async Task GetAllAsync_ReturnsHumans()
    {
        _repository.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Human>
        {
            new() { Id = "u1", Name = "User1" },
            new() { Id = "u2", Name = "User2" }
        });

        var result = (await _sut.GetAllAsync()).ToList();
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task GetByIdAsync_Found()
    {
        _repository.Setup(r => r.GetByIdAsync("u1")).ReturnsAsync(new Human { Id = "u1", Name = "User1" });
        var result = await _sut.GetByIdAsync("u1");
        Assert.NotNull(result);
        Assert.Equal("User1", result!.Name);
    }

    [Fact]
    public async Task GetByIdAsync_NotFound()
    {
        _repository.Setup(r => r.GetByIdAsync("unknown")).ReturnsAsync((Human?)null);
        Assert.Null(await _sut.GetByIdAsync("unknown"));
    }

    [Fact]
    public async Task GetByIdAndProfileAsync_Found()
    {
        _repository.Setup(r => r.GetByIdAndProfileAsync("u1", 1))
            .ReturnsAsync(new Human { Id = "u1", CurrentProfileNo = 1 });
        Assert.NotNull(await _sut.GetByIdAndProfileAsync("u1", 1));
    }

    [Fact]
    public async Task GetByIdAndProfileAsync_NotFound()
    {
        _repository.Setup(r => r.GetByIdAndProfileAsync("u1", 99)).ReturnsAsync((Human?)null);
        Assert.Null(await _sut.GetByIdAndProfileAsync("u1", 99));
    }

    [Fact]
    public async Task CreateAsync_Delegates()
    {
        var human = new Human { Id = "u1", Name = "New" };
        _repository.Setup(r => r.CreateAsync(human)).ReturnsAsync(human);
        var result = await _sut.CreateAsync(human);
        Assert.Equal("New", result.Name);
    }

    [Fact]
    public async Task UpdateAsync_Delegates()
    {
        var human = new Human { Id = "u1", Name = "Updated" };
        _repository.Setup(r => r.UpdateAsync(human)).ReturnsAsync(human);
        await _sut.UpdateAsync(human);
        _repository.Verify(r => r.UpdateAsync(human), Times.Once);
    }

    [Fact]
    public async Task ExistsAsync_ReturnsTrue() { _repository.Setup(r => r.ExistsAsync("u1")).ReturnsAsync(true); Assert.True(await _sut.ExistsAsync("u1")); }

    [Fact]
    public async Task ExistsAsync_ReturnsFalse() { _repository.Setup(r => r.ExistsAsync("unknown")).ReturnsAsync(false); Assert.False(await _sut.ExistsAsync("unknown")); }

    [Fact]
    public async Task DeleteAllAlarmsByUserAsync_ReturnsCount()
    {
        _repository.Setup(r => r.DeleteAllAlarmsByUserAsync("u1")).ReturnsAsync(15);
        Assert.Equal(15, await _sut.DeleteAllAlarmsByUserAsync("u1"));
    }

    [Fact]
    public async Task DeleteUserAsync_ReturnsTrue() { _repository.Setup(r => r.DeleteUserAsync("u1")).ReturnsAsync(true); Assert.True(await _sut.DeleteUserAsync("u1")); }

    [Fact]
    public async Task DeleteUserAsync_ReturnsFalse() { _repository.Setup(r => r.DeleteUserAsync("unknown")).ReturnsAsync(false); Assert.False(await _sut.DeleteUserAsync("unknown")); }
}
