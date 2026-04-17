using System.Text.Json;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

public class ProfileOverviewServiceTests
{
    private readonly Mock<IPoracleHumanProxy> _humanProxy = new();
    private readonly Mock<IPoracleTrackingProxy> _proxy = new();
    private readonly Mock<IFeatureGate> _featureGate = new();
    private readonly ProfileOverviewService _sut;

    public ProfileOverviewServiceTests()
    {
        this._featureGate.Setup(g => g.EnsureEnabledAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        this._sut = new ProfileOverviewService(this._proxy.Object, this._humanProxy.Object, this._featureGate.Object);
    }

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

    [Fact]
    public async Task DuplicateProfileAsyncThrowsBeforeMutatingStateWhenSourceContainsDisabledType()
    {
        // The pre-validation pass must run BEFORE SwitchProfileAsync — otherwise a partial
        // duplicate could fail mid-loop and leave the new profile half-populated. (#236)
        var humanJson = CreateJsonObject(new
        {
            current_profile_no = 1
        });
        this._humanProxy.Setup(h => h.GetHumanAsync("u1")).ReturnsAsync(humanJson);
        this._humanProxy.Setup(h => h.SwitchProfileAsync("u1", It.IsAny<int>())).Returns(Task.CompletedTask);

        var allTracking = CreateJsonObject(new
        {
            pokemon = new[] { new { uid = 10, profile_no = 2, pokemon_id = 25 } }
        });
        this._proxy.Setup(p => p.GetAllTrackingAllProfilesAsync("u1")).ReturnsAsync(allTracking);

        this._featureGate
            .Setup(g => g.EnsureEnabledAsync(DisableFeatureKeys.Pokemon))
            .ThrowsAsync(new FeatureDisabledException(DisableFeatureKeys.Pokemon));

        var ex = await Assert.ThrowsAsync<FeatureDisabledException>(
            () => this._sut.DuplicateProfileAsync("u1", 2, 5));

        Assert.Equal(DisableFeatureKeys.Pokemon, ex.DisableKey);
        this._humanProxy.Verify(h => h.SwitchProfileAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        this._proxy.Verify(p => p.CreateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<JsonElement>()), Times.Never);
    }

    [Fact]
    public async Task DuplicateProfileAsyncIgnoresDisabledTypesWithNoMatchingAlarmsInSource()
    {
        // The pokemon entries are all on a different profile, so disable_mons=true shouldn't
        // block the duplicate — there are no monster alarms actually being copied.
        var humanJson = CreateJsonObject(new
        {
            current_profile_no = 1
        });
        this._humanProxy.Setup(h => h.GetHumanAsync("u1")).ReturnsAsync(humanJson);
        this._humanProxy.Setup(h => h.SwitchProfileAsync("u1", It.IsAny<int>())).Returns(Task.CompletedTask);

        var allTracking = CreateJsonObject(new
        {
            pokemon = new[] { new { uid = 10, profile_no = 99, pokemon_id = 25 } },
            raid = new[] { new { uid = 30, profile_no = 2, pokemon_id = 386 } }
        });
        this._proxy.Setup(p => p.GetAllTrackingAllProfilesAsync("u1")).ReturnsAsync(allTracking);
        this._proxy
            .Setup(p => p.CreateAsync(It.IsAny<string>(), "u1", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([100], 0, 0, 1));

        this._featureGate
            .Setup(g => g.EnsureEnabledAsync(DisableFeatureKeys.Pokemon))
            .ThrowsAsync(new FeatureDisabledException(DisableFeatureKeys.Pokemon));
        this._featureGate
            .Setup(g => g.EnsureEnabledAsync(DisableFeatureKeys.Raids))
            .Returns(Task.CompletedTask);

        var result = await this._sut.DuplicateProfileAsync("u1", 2, 5);

        Assert.Equal(1, result); // only the raid alarm in profile 2 was copied
    }

    [Fact]
    public async Task ImportAlarmsAsyncThrowsBeforeMutatingStateWhenPayloadContainsDisabledType()
    {
        var humanJson = CreateJsonObject(new
        {
            current_profile_no = 1
        });
        this._humanProxy.Setup(h => h.GetHumanAsync("u1")).ReturnsAsync(humanJson);
        this._humanProxy.Setup(h => h.SwitchProfileAsync("u1", It.IsAny<int>())).Returns(Task.CompletedTask);

        var alarms = CreateJsonObject(new
        {
            invasion = new[] { new { grunt_type = "fire" } }
        });

        this._featureGate
            .Setup(g => g.EnsureEnabledAsync(DisableFeatureKeys.Invasions))
            .ThrowsAsync(new FeatureDisabledException(DisableFeatureKeys.Invasions));

        var ex = await Assert.ThrowsAsync<FeatureDisabledException>(
            () => this._sut.ImportAlarmsAsync("u1", 5, alarms));

        Assert.Equal(DisableFeatureKeys.Invasions, ex.DisableKey);
        this._humanProxy.Verify(h => h.SwitchProfileAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        this._proxy.Verify(p => p.CreateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<JsonElement>()), Times.Never);
    }

    private static JsonElement CreateJsonObject(object obj)
    {
        var json = JsonSerializer.Serialize(obj);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
