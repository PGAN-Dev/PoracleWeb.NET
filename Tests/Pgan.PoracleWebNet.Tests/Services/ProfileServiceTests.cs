using System.Text.Json;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Models.Helpers;
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

    [Theory]
    [InlineData("\"[\\\"downtown\\\"]\"")]
    [InlineData("[\"downtown\"]")]
    public async Task GetByUserAsyncSynthesisesTheActiveProfileFromEitherAreaShape(string areaLiteral)
    {
        // PoracleNG has no profiles row yet, so the active profile is built from the human. Since
        // jfberry/PoracleNG#303 that human's area is an array; 5.1.0/5.2.1 send a JSON-encoded string.
        this._humanProxy.Setup(p => p.GetProfilesAsync("u1"))
            .ReturnsAsync(JsonSerializer.SerializeToElement(new { profile = Array.Empty<object>(), status = "ok" }));
        var human = JsonDocument.Parse(
            $$"""{"id":"u1","current_profile_no":1,"area":{{areaLiteral}},"latitude":1.5,"longitude":2.5}""").RootElement.Clone();
        this._humanProxy.Setup(p => p.GetHumanAsync("u1")).ReturnsAsync(human);

        var result = (await this._sut.GetByUserAsync("u1")).ToList();

        var synthesised = Assert.Single(result);
        Assert.Equal(1, synthesised.ProfileNo);
        Assert.Equal(["downtown"], AreaListJson.Parse(synthesised.Area));
    }
}
