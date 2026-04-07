using System.Text.Json;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

public class CrossProfileServiceTests
{
    private readonly Mock<IPoracleHumanProxy> _humanProxy = new();
    private readonly Mock<IPoracleTrackingProxy> _proxy = new();
    private readonly CrossProfileService _sut;

    public CrossProfileServiceTests() => this._sut = new CrossProfileService(this._proxy.Object, this._humanProxy.Object);

    [Fact]
    public async Task GetAllProfilesOverviewAsyncReturnsProxyResult()
    {
        var expected = CreateJsonObject(new
        {
            pokemon = new[] { new { uid = 1 } },
            raid = new[] { new { uid = 2 } }
        });
        this._proxy.Setup(p => p.GetAllTrackingAllProfilesAsync("u1")).ReturnsAsync(expected);

        var result = await this._sut.GetAllProfilesOverviewAsync("u1");

        Assert.Equal(expected.ToString(), result.ToString());
    }

    [Fact]
    public async Task GetAllProfilesOverviewAsyncPassesUserIdToProxy()
    {
        var json = CreateJsonObject(new
        {
        });
        this._proxy.Setup(p => p.GetAllTrackingAllProfilesAsync("user42")).ReturnsAsync(json);

        await this._sut.GetAllProfilesOverviewAsync("user42");

        this._proxy.Verify(p => p.GetAllTrackingAllProfilesAsync("user42"), Times.Once);
    }

    [Fact]
    public async Task DuplicateProfileAsyncRestoresProfileOnError()
    {
        // Current profile is 1
        var humanJson = CreateJsonObject(new
        {
            current_profile_no = 1
        });
        this._humanProxy.Setup(h => h.GetHumanAsync("u1")).ReturnsAsync(humanJson);
        this._humanProxy.Setup(h => h.SwitchProfileAsync("u1", 5)).Returns(Task.CompletedTask);

        // Return alarms on the source profile
        var allTracking = CreateJsonObject(new
        {
            pokemon = new[] { new { uid = 10, profile_no = 2 } }
        });
        this._proxy.Setup(p => p.GetAllTrackingAllProfilesAsync("u1")).ReturnsAsync(allTracking);

        // CreateAsync throws after switching to the new profile
        this._proxy
            .Setup(p => p.CreateAsync("pokemon", "u1", It.IsAny<JsonElement>()))
            .ThrowsAsync(new HttpRequestException("API error"));

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            this._sut.DuplicateProfileAsync("u1", 2, 5));

        // Verify profile was restored to original (1)
        this._humanProxy.Verify(h => h.SwitchProfileAsync("u1", 1), Times.Once);
    }

    [Fact]
    public async Task ImportAlarmsAsyncRestoresProfileOnError()
    {
        var humanJson = CreateJsonObject(new
        {
            current_profile_no = 3
        });
        this._humanProxy.Setup(h => h.GetHumanAsync("u1")).ReturnsAsync(humanJson);
        this._humanProxy.Setup(h => h.SwitchProfileAsync("u1", 5)).Returns(Task.CompletedTask);

        var alarms = CreateJsonObject(new
        {
            pokemon = new[] { new { pokemon_id = 1 } }
        });

        this._proxy
            .Setup(p => p.CreateAsync("pokemon", "u1", It.IsAny<JsonElement>()))
            .ThrowsAsync(new HttpRequestException("API error"));

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            this._sut.ImportAlarmsAsync("u1", 5, alarms));

        // Verify profile was restored to original (3)
        this._humanProxy.Verify(h => h.SwitchProfileAsync("u1", 3), Times.Once);
    }

    [Fact]
    public async Task DuplicateProfileAsyncCopiesAlarmsFromSourceProfile()
    {
        var humanJson = CreateJsonObject(new
        {
            current_profile_no = 1
        });
        this._humanProxy.Setup(h => h.GetHumanAsync("u1")).ReturnsAsync(humanJson);
        this._humanProxy.Setup(h => h.SwitchProfileAsync("u1", It.IsAny<int>())).Returns(Task.CompletedTask);

        // Two alarms: one on source profile 2, one on a different profile 3
        var allTracking = CreateJsonObject(new
        {
            pokemon = new[]
            {
                new { uid = 10, profile_no = 2, pokemon_id = 25 },
                new { uid = 20, profile_no = 3, pokemon_id = 150 }
            },
            raid = new[]
            {
                new { uid = 30, profile_no = 2, pokemon_id = 386 }
            }
        });
        this._proxy.Setup(p => p.GetAllTrackingAllProfilesAsync("u1")).ReturnsAsync(allTracking);
        this._proxy
            .Setup(p => p.CreateAsync(It.IsAny<string>(), "u1", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([100], 0, 0, 1));

        var result = await this._sut.DuplicateProfileAsync("u1", 2, 5);

        // Only 2 alarms from source profile 2 (pokemon uid=10 + raid uid=30), not the one from profile 3
        Assert.Equal(2, result);
        this._proxy.Verify(
            p => p.CreateAsync(It.IsAny<string>(), "u1", It.IsAny<JsonElement>()),
            Times.Exactly(2));
    }

    private static JsonElement CreateJsonObject(object obj)
    {
        var json = JsonSerializer.Serialize(obj);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
