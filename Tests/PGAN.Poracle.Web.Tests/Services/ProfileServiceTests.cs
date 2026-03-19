using Moq;
using PGAN.Poracle.Web.Core.Abstractions.Repositories;
using PGAN.Poracle.Web.Core.Models;
using PGAN.Poracle.Web.Core.Services;

namespace PGAN.Poracle.Web.Tests.Services;

public class ProfileServiceTests
{
    private readonly Mock<IProfileRepository> _repository = new();
    private readonly ProfileService _sut;

    public ProfileServiceTests() => this._sut = new ProfileService(this._repository.Object);

    [Fact]
    public async Task GetByUserAsyncReturnsProfiles()
    {
        this._repository.Setup(r => r.GetByUserAsync("u1")).ReturnsAsync(new List<Profile>
        {
            new() { Id = "u1", ProfileNo = 1, Name = "Default" },
            new() { Id = "u1", ProfileNo = 2, Name = "PvP" }
        });

        var result = (await this._sut.GetByUserAsync("u1")).ToList();

        Assert.Equal(2, result.Count);
        Assert.Equal("Default", result[0].Name);
    }

    [Fact]
    public async Task GetByUserAndProfileNoAsyncReturnsProfile()
    {
        this._repository.Setup(r => r.GetByUserAndProfileNoAsync("u1", 1))
            .ReturnsAsync(new Profile { Id = "u1", ProfileNo = 1, Name = "Default" });

        var result = await this._sut.GetByUserAndProfileNoAsync("u1", 1);

        Assert.NotNull(result);
        Assert.Equal("Default", result!.Name);
    }

    [Fact]
    public async Task GetByUserAndProfileNoAsyncReturnsNullWhenNotFound()
    {
        this._repository.Setup(r => r.GetByUserAndProfileNoAsync("u1", 99)).ReturnsAsync((Profile?)null);
        Assert.Null(await this._sut.GetByUserAndProfileNoAsync("u1", 99));
    }

    [Fact]
    public async Task CreateAsyncDelegates()
    {
        var profile = new Profile { Id = "u1", ProfileNo = 3, Name = "New" };
        this._repository.Setup(r => r.CreateAsync(profile)).ReturnsAsync(profile);

        var result = await this._sut.CreateAsync(profile);

        Assert.Equal("New", result.Name);
        this._repository.Verify(r => r.CreateAsync(profile), Times.Once);
    }

    [Fact]
    public async Task UpdateAsyncDelegates()
    {
        var profile = new Profile { Id = "u1", ProfileNo = 1, Name = "Updated" };
        this._repository.Setup(r => r.UpdateAsync(profile)).ReturnsAsync(profile);
        await this._sut.UpdateAsync(profile);
        this._repository.Verify(r => r.UpdateAsync(profile), Times.Once);
    }

    [Fact]
    public async Task DeleteAsyncReturnsTrue()
    {
        this._repository.Setup(r => r.DeleteAsync("u1", 2)).ReturnsAsync(true);
        Assert.True(await this._sut.DeleteAsync("u1", 2));
    }

    [Fact]
    public async Task DeleteAsyncReturnsFalse()
    {
        this._repository.Setup(r => r.DeleteAsync("u1", 99)).ReturnsAsync(false);
        Assert.False(await this._sut.DeleteAsync("u1", 99));
    }
}
