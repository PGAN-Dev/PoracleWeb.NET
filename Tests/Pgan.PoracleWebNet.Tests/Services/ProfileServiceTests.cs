using System.Text.Json;
using Microsoft.Extensions.Logging;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

public class ProfileServiceTests
{
    private readonly Mock<IProfileRepository> _repository = new();
    private readonly Mock<IPoracleHumanProxy> _humanProxy = new();
    private readonly ProfileService _sut;

    public ProfileServiceTests() => this._sut = new ProfileService(
        this._repository.Object, this._humanProxy.Object);

    [Fact]
    public async Task GetByUserAsyncReturnsProfilesFromProxy()
    {
        var proxyResponse = JsonSerializer.SerializeToElement(new
        {
            profile = new[]
            {
                new { id = "u1", profile_no = 1, name = "Default", area = "[]", latitude = 0.0, longitude = 0.0 },
                new { id = "u1", profile_no = 2, name = "PvP", area = "[]", latitude = 0.0, longitude = 0.0 }
            },
            status = "ok"
        });
        this._humanProxy.Setup(p => p.GetProfilesAsync("u1")).ReturnsAsync(proxyResponse);

        var result = (await this._sut.GetByUserAsync("u1")).ToList();

        Assert.Equal(2, result.Count);
        Assert.Equal("Default", result[0].Name);
        Assert.Equal("PvP", result[1].Name);
    }

    [Fact]
    public async Task GetByUserAsyncThrowsOnProxyFailure()
    {
        this._humanProxy.Setup(p => p.GetProfilesAsync("u1")).ThrowsAsync(new HttpRequestException("Connection refused"));
        await Assert.ThrowsAsync<HttpRequestException>(() => this._sut.GetByUserAsync("u1"));
    }

    [Fact]
    public async Task GetByUserAndProfileNoAsyncReturnsProfileFromProxy()
    {
        var proxyResponse = JsonSerializer.SerializeToElement(new
        {
            profile = new[]
            {
                new { id = "u1", profile_no = 1, name = "Default", area = "[]", latitude = 0.0, longitude = 0.0 }
            },
            status = "ok"
        });
        this._humanProxy.Setup(p => p.GetProfilesAsync("u1")).ReturnsAsync(proxyResponse);

        var result = await this._sut.GetByUserAndProfileNoAsync("u1", 1);

        Assert.NotNull(result);
        Assert.Equal("Default", result!.Name);
    }

    [Fact]
    public async Task GetByUserAndProfileNoAsyncReturnsNullWhenNotFound()
    {
        var proxyResponse = JsonSerializer.SerializeToElement(new
        {
            profile = new[]
            {
                new { id = "u1", profile_no = 1, name = "Default", area = "[]", latitude = 0.0, longitude = 0.0 }
            },
            status = "ok"
        });
        this._humanProxy.Setup(p => p.GetProfilesAsync("u1")).ReturnsAsync(proxyResponse);

        // Profile 99 doesn't exist — proxy returns profiles but none match
        // Falls back to DB which also returns null
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
