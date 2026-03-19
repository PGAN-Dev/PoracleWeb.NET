using Moq;
using PGAN.Poracle.Web.Core.Abstractions.Repositories;
using PGAN.Poracle.Web.Core.Models;
using PGAN.Poracle.Web.Core.Services;

namespace PGAN.Poracle.Web.Tests.Services;

public class HumanServiceTests
{
    private readonly Mock<IHumanRepository> _repository = new();
    private readonly HumanService _sut;

    public HumanServiceTests() => this._sut = new HumanService(this._repository.Object);

    [Fact]
    public async Task GetAllAsyncReturnsHumans()
    {
        this._repository.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Human>
        {
            new() { Id = "u1", Name = "User1" },
            new() { Id = "u2", Name = "User2" }
        });

        var result = (await this._sut.GetAllAsync()).ToList();
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task GetByIdAsyncFound()
    {
        this._repository.Setup(r => r.GetByIdAsync("u1")).ReturnsAsync(new Human { Id = "u1", Name = "User1" });
        var result = await this._sut.GetByIdAsync("u1");
        Assert.NotNull(result);
        Assert.Equal("User1", result!.Name);
    }

    [Fact]
    public async Task GetByIdAsyncNotFound()
    {
        this._repository.Setup(r => r.GetByIdAsync("unknown")).ReturnsAsync((Human?)null);
        Assert.Null(await this._sut.GetByIdAsync("unknown"));
    }

    [Fact]
    public async Task GetByIdAndProfileAsyncFound()
    {
        this._repository.Setup(r => r.GetByIdAndProfileAsync("u1", 1))
            .ReturnsAsync(new Human { Id = "u1", CurrentProfileNo = 1 });
        Assert.NotNull(await this._sut.GetByIdAndProfileAsync("u1", 1));
    }

    [Fact]
    public async Task GetByIdAndProfileAsyncNotFound()
    {
        this._repository.Setup(r => r.GetByIdAndProfileAsync("u1", 99)).ReturnsAsync((Human?)null);
        Assert.Null(await this._sut.GetByIdAndProfileAsync("u1", 99));
    }

    [Fact]
    public async Task CreateAsyncDelegates()
    {
        var human = new Human { Id = "u1", Name = "New" };
        this._repository.Setup(r => r.CreateAsync(human)).ReturnsAsync(human);
        var result = await this._sut.CreateAsync(human);
        Assert.Equal("New", result.Name);
    }

    [Fact]
    public async Task UpdateAsyncDelegates()
    {
        var human = new Human { Id = "u1", Name = "Updated" };
        this._repository.Setup(r => r.UpdateAsync(human)).ReturnsAsync(human);
        await this._sut.UpdateAsync(human);
        this._repository.Verify(r => r.UpdateAsync(human), Times.Once);
    }

    [Fact]
    public async Task ExistsAsyncReturnsTrue()
    {
        this._repository.Setup(r => r.ExistsAsync("u1")).ReturnsAsync(true);
        Assert.True(await this._sut.ExistsAsync("u1"));
    }

    [Fact]
    public async Task ExistsAsyncReturnsFalse()
    {
        this._repository.Setup(r => r.ExistsAsync("unknown")).ReturnsAsync(false);
        Assert.False(await this._sut.ExistsAsync("unknown"));
    }

    [Fact]
    public async Task DeleteAllAlarmsByUserAsyncReturnsCount()
    {
        this._repository.Setup(r => r.DeleteAllAlarmsByUserAsync("u1")).ReturnsAsync(15);
        Assert.Equal(15, await this._sut.DeleteAllAlarmsByUserAsync("u1"));
    }

    [Fact]
    public async Task DeleteUserAsyncReturnsTrue()
    {
        this._repository.Setup(r => r.DeleteUserAsync("u1")).ReturnsAsync(true);
        Assert.True(await this._sut.DeleteUserAsync("u1"));
    }

    [Fact]
    public async Task DeleteUserAsyncReturnsFalse()
    {
        this._repository.Setup(r => r.DeleteUserAsync("unknown")).ReturnsAsync(false);
        Assert.False(await this._sut.DeleteUserAsync("unknown"));
    }
}
