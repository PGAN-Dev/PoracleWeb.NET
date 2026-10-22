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
    private readonly Mock<IPoracleIncidentProxy> _incident = new();
    private readonly ProfileOverviewService _sut;

    public ProfileOverviewServiceTests()
    {
        this._featureGate.Setup(g => g.EnsureEnabledAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        this._sut = new ProfileOverviewService(this._proxy.Object, this._humanProxy.Object, this._featureGate.Object, this._incident.Object);
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

    /// <summary>
    /// Changed deliberately, with the copy (#993). This used to assert that a failed per-alarm create
    /// switched the user back to their original profile. The duplicate now uses PoracleNG's own copy, which
    /// never switches, so there is nothing to restore; what must still hold is that a failure reaches the
    /// controller, which deletes the half-made profile.
    /// </summary>
    [Fact]
    public async Task DuplicateProfileAsyncLetsACopyFailureThroughWithoutEverSwitchingProfile()
    {
        var allTracking = CreateJsonObject(new
        {
            pokemon = new[] { new { uid = 10, profile_no = 2 } }
        });
        this._proxy.Setup(p => p.GetAllTrackingAllProfilesAsync("u1")).ReturnsAsync(allTracking);
        this._humanProxy
            .Setup(h => h.CopyProfileAsync("u1", 2, 5))
            .ThrowsAsync(new HttpRequestException("API error"));

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            this._sut.DuplicateProfileAsync("u1", 2, 5));

        this._humanProxy.Verify(h => h.SwitchProfileAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    /// <summary>
    /// Replaces ImportAlarmsAsyncRestoresProfileOnError, which asserted the switch-and-restore around the
    /// per-alarm create. Import now writes to the target profile with ?profile=N, so the active profile is
    /// never moved and there is nothing to restore; a failure still reaches the caller.
    /// </summary>
    [Fact]
    public async Task ImportNeverSwitchesProfile()
    {
        var alarms = CreateJsonObject(new { pokemon = new[] { new { pokemon_id = 1 } } });
        this._proxy
            .Setup(p => p.CreateOnProfileAsync("pokemon", "u1", It.IsAny<JsonElement>(), 5))
            .ThrowsAsync(new HttpRequestException("API error"));

        await Assert.ThrowsAsync<HttpRequestException>(() => this._sut.ImportAlarmsAsync("u1", 5, alarms));

        this._humanProxy.Verify(h => h.SwitchProfileAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        this._humanProxy.Verify(h => h.GetHumanAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ImportOneCreatePerTypeWithTargetProfile()
    {
        var alarms = CreateJsonObject(new
        {
            pokemon = new object[] { new { uid = 9, profile_no = 7, id = "x", pokemon_id = 1 }, new { pokemon_id = 2 } },
            raid = new[] { new { pokemon_id = 386 } },
        });
        var bodies = new Dictionary<string, JsonElement>();
        this._proxy
            .Setup(p => p.CreateOnProfileAsync(It.IsAny<string>(), "u1", It.IsAny<JsonElement>(), 5))
            .Callback<string, string, JsonElement, int>((t, _, b, _) => bodies[t] = b.Clone())
            .ReturnsAsync((string _, string _, JsonElement b, int _) =>
                new TrackingCreateResult([], 0, 0, b.GetArrayLength()));

        var created = await this._sut.ImportAlarmsAsync("u1", 5, alarms);

        Assert.Equal(3, created);
        Assert.Equal(["pokemon", "raid"], bodies.Keys.OrderBy(k => k).ToArray());
        this._proxy.Verify(
            p => p.CreateOnProfileAsync(It.IsAny<string>(), "u1", It.IsAny<JsonElement>(), 5), Times.Exactly(2));
        this._proxy.Verify(p => p.CreateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<JsonElement>()), Times.Never);

        Assert.Equal(JsonValueKind.Array, bodies["pokemon"].ValueKind);
        Assert.Equal(2, bodies["pokemon"].GetArrayLength());
        foreach (var row in bodies["pokemon"].EnumerateArray())
        {
            Assert.False(row.TryGetProperty("uid", out _));
            Assert.False(row.TryGetProperty("profile_no", out _));
            Assert.False(row.TryGetProperty("id", out _));
        }
    }

    [Fact]
    public async Task ImportIdenticalRulesOnTargetAreNotCounted()
    {
        var alarms = CreateJsonObject(new { pokemon = new[] { new { pokemon_id = 1 }, new { pokemon_id = 2 }, new { pokemon_id = 3 } } });
        this._proxy
            .Setup(p => p.CreateOnProfileAsync("pokemon", "u1", It.IsAny<JsonElement>(), 5))
            .ReturnsAsync(new TrackingCreateResult([100], 1, 0, 1, null, 1));

        var created = await this._sut.ImportAlarmsAsync("u1", 5, alarms);

        Assert.Equal(1, created);
    }

    [Fact]
    public async Task ImportStillRefusesInvalidFileBeforeAnyWrite()
    {
        var alarms = CreateJsonObject(new
        {
            raid = new[] { new { pokemon_id = 386 } },
            pokemon = new[] { new { pokemon_id = 1, distance = -77 } },
        });

        await Assert.ThrowsAsync<AlarmValidationException>(() => this._sut.ImportAlarmsAsync("u1", 5, alarms));

        this._proxy.Verify(
            p => p.CreateOnProfileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<JsonElement>(), It.IsAny<int>()),
            Times.Never);
        this._humanProxy.Verify(h => h.SwitchProfileAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
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

        // PoracleNG's copy, source to destination, in that order: inverting them would replace the
        // source's rules with the empty new profile's. Nothing is re-created one rule at a time (#993).
        this._humanProxy.Verify(h => h.CopyProfileAsync("u1", 2, 5), Times.Once);
        this._proxy.Verify(
            p => p.CreateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<JsonElement>()),
            Times.Never);
        this._humanProxy.Verify(h => h.SwitchProfileAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
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

        // PoracleNG's copy does not know about disable_* switches; refusing before it runs is the gate.
        this._humanProxy.Verify(
            h => h.CopyProfileAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
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

    // ── Pokestop events and legacy grunt names in an import ─────────────────
    // An export folds Pokestop events into the invasion array (as v1 held them), and older rules say metal.
    // v2's invasion route refuses both, and a refusal there came after the earlier types were written.

    [Fact]
    public async Task ImportSendsEventGruntsToIncidentOnTheTargetProfileAndMetalAsSteel()
    {
        var alarms = CreateJsonObject(new
        {
            pokemon = new[] { new { pokemon_id = 1 } },
            invasion = new object[]
            {
                new { uid = 289, grunt_type = "kecleon", gender = 0, distance = 500, template = "2", clean = 1 },
                new { grunt_type = "metal", gender = 1 },
            },
        });
        var bodies = new Dictionary<string, JsonElement>();
        this._proxy
            .Setup(p => p.CreateOnProfileAsync(It.IsAny<string>(), "u1", It.IsAny<JsonElement>(), 5))
            .Callback<string, string, JsonElement, int>((t, _, b, _) => bodies[t] = b.Clone())
            .ReturnsAsync((string _, string _, JsonElement b, int _) => new TrackingCreateResult([], 0, 0, b.GetArrayLength()));
        List<PokestopEvent>? committed = null;
        this._incident
            .Setup(p => p.CreateAsync("u1", It.IsAny<IEnumerable<PokestopEvent>>(), 5, It.IsAny<bool>()))
            .ReturnsAsync((string _, IEnumerable<PokestopEvent> rules, int _, bool dryRun) =>
            {
                List<PokestopEvent> list = [.. rules];
                if (!dryRun)
                {
                    committed = list;
                }

                return new PokestopEventWriteResult(
                    [.. list.Select(r => new PokestopEvent { Uid = dryRun ? 0 : 300, DisplayType = r.DisplayType })], [], []);
            });

        var created = await this._sut.ImportAlarmsAsync("u1", 5, alarms);

        Assert.Equal(3, created);
        var invasion = Assert.Single(bodies["invasion"].EnumerateArray());
        Assert.Equal("steel", invasion.GetProperty("grunt_type").GetString());
        Assert.DoesNotContain("kecleon", bodies["invasion"].GetRawText(), StringComparison.Ordinal);
        var rule = Assert.Single(committed!);
        Assert.Equal(PokestopEventTypes.Kecleon, rule.DisplayType);
        Assert.Equal(500, rule.Distance);
        Assert.Equal("2", rule.Template);
        Assert.Equal(1, rule.Clean);
        this._incident.Verify(
            p => p.CreateAsync(It.IsAny<string>(), It.IsAny<IEnumerable<PokestopEvent>>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task ImportOfOnlyEventGruntsMakesNoInvasionWrite()
    {
        var alarms = CreateJsonObject(new { invasion = new[] { new { grunt_type = "showcase", gender = 0 } } });
        this._incident
            .Setup(p => p.CreateAsync("u1", It.IsAny<IEnumerable<PokestopEvent>>(), 5, It.IsAny<bool>()))
            .ReturnsAsync((string _, IEnumerable<PokestopEvent> rules, int _, bool dryRun) =>
                new PokestopEventWriteResult([new PokestopEvent { Uid = dryRun ? 0 : 301, DisplayType = PokestopEventTypes.Showcase }], [], []));

        Assert.Equal(1, await this._sut.ImportAlarmsAsync("u1", 5, alarms));
        this._proxy.Verify(
            p => p.CreateOnProfileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<JsonElement>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task ImportRefusesAnEventWithAGenderBeforeWritingAnything()
    {
        // The event row comes after pokemon in the file's write order; the refusal must still come first.
        var alarms = CreateJsonObject(new
        {
            pokemon = new[] { new { pokemon_id = 1 } },
            invasion = new[] { new { grunt_type = "gold-stop", gender = 2 } },
        });

        var ex = await Assert.ThrowsAsync<AlarmValidationException>(() => this._sut.ImportAlarmsAsync("u1", 5, alarms));

        Assert.StartsWith("PoracleNG can't store this alarm as it is:", ex.Message, StringComparison.Ordinal);
        this._proxy.Verify(
            p => p.CreateOnProfileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<JsonElement>(), It.IsAny<int>()), Times.Never);
        this._incident.Verify(
            p => p.CreateAsync(It.IsAny<string>(), It.IsAny<IEnumerable<PokestopEvent>>(), It.IsAny<int>(), It.IsAny<bool>()), Times.Never);
    }

    private static JsonElement CreateJsonObject(object obj)
    {
        var json = JsonSerializer.Serialize(obj);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
