using System.Text.Json;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

public class ProfileServiceTests
{
    private readonly Mock<IPoracleHumanProxy> _humanProxy = new();
    private readonly ProfileService _sut;

    public ProfileServiceTests() => this._sut = new ProfileService(this._humanProxy.Object);

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

        // Profile 99 does not exist and there is no DB fallback: the proxy's answer is the answer.
        Assert.Null(await this._sut.GetByUserAndProfileNoAsync("u1", 99));
    }

    [Fact]
    public async Task CopyAsyncCallsProxy()
    {
        await this._sut.CopyAsync("u1", 1, 2);
        this._humanProxy.Verify(p => p.CopyProfileAsync("u1", 1, 2), Times.Once);
    }

    [Fact]
    public async Task GetByUserAsyncDeserializesActiveHoursFromProxy()
    {
        var proxyResponse = JsonSerializer.SerializeToElement(new
        {
            profile = new[]
            {
                new
                {
                    id = "u1",
                    profile_no = 1,
                    name = "Default",
                    area = "[]",
                    latitude = 0.0,
                    longitude = 0.0,
                    active_hours = /*lang=json,strict*/ "[{\"day\":1,\"hours\":\"09\",\"mins\":\"00\"}]"
                }
            },
            status = "ok"
        });
        this._humanProxy.Setup(p => p.GetProfilesAsync("u1")).ReturnsAsync(proxyResponse);

        var result = (await this._sut.GetByUserAsync("u1")).ToList();

        Assert.Single(result);
        Assert.Equal(/*lang=json,strict*/ "[{\"day\":1,\"hours\":\"09\",\"mins\":\"00\"}]", result[0].ActiveHours);
    }

    [Fact]
    public async Task GetByUserAsyncHandlesNullActiveHours()
    {
        var proxyResponse = JsonSerializer.SerializeToElement(new
        {
            profile = new[]
            {
                new
                {
                    id = "u1",
                    profile_no = 1,
                    name = "Default",
                    area = "[]",
                    latitude = 0.0,
                    longitude = 0.0
                }
            },
            status = "ok"
        });
        this._humanProxy.Setup(p => p.GetProfilesAsync("u1")).ReturnsAsync(proxyResponse);

        var result = (await this._sut.GetByUserAsync("u1")).ToList();

        Assert.Single(result);
        Assert.Null(result[0].ActiveHours);
    }

    [Fact]
    public async Task GetByUserAsyncHandlesEmptyActiveHours()
    {
        var proxyResponse = JsonSerializer.SerializeToElement(new
        {
            profile = new[]
            {
                new
                {
                    id = "u1",
                    profile_no = 1,
                    name = "Default",
                    area = "[]",
                    latitude = 0.0,
                    longitude = 0.0,
                    active_hours = "[]"
                }
            },
            status = "ok"
        });
        this._humanProxy.Setup(p => p.GetProfilesAsync("u1")).ReturnsAsync(proxyResponse);

        var result = (await this._sut.GetByUserAsync("u1")).ToList();

        Assert.Single(result);
        Assert.Equal("[]", result[0].ActiveHours);
    }
}
