using Moq;
using PGAN.Poracle.Web.Core.Abstractions.Repositories;
using PGAN.Poracle.Web.Core.Models;
using PGAN.Poracle.Web.Core.Services;

namespace PGAN.Poracle.Web.Tests.Services;

public class ProfileServiceTests
{
    private readonly Mock<IProfileRepository> _repository = new();
    private readonly ProfileService _sut;

    public ProfileServiceTests() => _sut = new ProfileService(_repository.Object);

    [Fact]
    public async Task GetByUserAsync_ReturnsProfiles()
    {
        _repository.Setup(r => r.GetByUserAsync("u1")).ReturnsAsync(new List<Profile>
        {
            new() { Id = "u1", ProfileNo = 1, Name = "Default" },
            new() { Id = "u1", ProfileNo = 2, Name = "PvP" }
        });

        var result = (await _sut.GetByUserAsync("u1")).ToList();

        Assert.Equal(2, result.Count);
        Assert.Equal("Default", result[0].Name);
    }

    [Fact]
    public async Task GetByUserAndProfileNoAsync_ReturnsProfile()
    {
        _repository.Setup(r => r.GetByUserAndProfileNoAsync("u1", 1))
            .ReturnsAsync(new Profile { Id = "u1", ProfileNo = 1, Name = "Default" });

        var result = await _sut.GetByUserAndProfileNoAsync("u1", 1);

        Assert.NotNull(result);
        Assert.Equal("Default", result!.Name);
    }

    [Fact]
    public async Task GetByUserAndProfileNoAsync_ReturnsNull_WhenNotFound()
    {
        _repository.Setup(r => r.GetByUserAndProfileNoAsync("u1", 99)).ReturnsAsync((Profile?)null);
        Assert.Null(await _sut.GetByUserAndProfileNoAsync("u1", 99));
    }

    [Fact]
    public async Task CreateAsync_Delegates()
    {
        var profile = new Profile { Id = "u1", ProfileNo = 3, Name = "New" };
        _repository.Setup(r => r.CreateAsync(profile)).ReturnsAsync(profile);

        var result = await _sut.CreateAsync(profile);

        Assert.Equal("New", result.Name);
        _repository.Verify(r => r.CreateAsync(profile), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_Delegates()
    {
        var profile = new Profile { Id = "u1", ProfileNo = 1, Name = "Updated" };
        _repository.Setup(r => r.UpdateAsync(profile)).ReturnsAsync(profile);
        await _sut.UpdateAsync(profile);
        _repository.Verify(r => r.UpdateAsync(profile), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_ReturnsTrue() { _repository.Setup(r => r.DeleteAsync("u1", 2)).ReturnsAsync(true); Assert.True(await _sut.DeleteAsync("u1", 2)); }

    [Fact]
    public async Task DeleteAsync_ReturnsFalse() { _repository.Setup(r => r.DeleteAsync("u1", 99)).ReturnsAsync(false); Assert.False(await _sut.DeleteAsync("u1", 99)); }
}
