using System.Text.Json;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// A PoracleNG without personal-shiny support answers 200 to a rule carrying <c>personal</c> and stores
/// it without the bit, so a "shiny only" rule would match every spawn. The write has to be refused, on
/// every path that reaches the server, and a rule that does not use the field has to keep saving.
/// </summary>
public class PersonalTrackingRefusalTests
{
    private const string Refusal = "Personal shiny and background alerts need a PoracleNG with personal-shiny support.";

    private readonly Mock<IPoracleHumanProxy> _humanProxy = new();
    private readonly Mock<IFeatureGate> _featureGate = new();
    private readonly Mock<IPoracleTrackingProxy> _proxy = new();
    private readonly Mock<ITrackedUidRemapper> _remapper = new();

    public PersonalTrackingRefusalTests()
    {
        this._featureGate.Setup(g => g.EnsureEnabledAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        this._proxy
            .Setup(p => p.GetByUserAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(Json("[]"));
        this._proxy
            .Setup(p => p.CreateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([9], 0, 0, 1));
        this._proxy
            .Setup(p => p.UpdateByUidAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingUpdateResult(1, false));
    }

    // --- MonsterService -----------------------------------------------------------------------------

    [Fact]
    public async Task CreateRefusesPersonalWithoutServerSupport()
    {
        var ex = await Assert.ThrowsAsync<AlarmValidationException>(
            () => this.Monsters(supported: false).CreateAsync("u1", new Monster { PokemonId = 25, Personal = 1 }));

        Assert.Equal(Refusal, ex.Message);
        this.VerifyNothingWritten();
    }

    [Fact]
    public async Task BulkCreateRefusesPersonalWithoutServerSupport()
    {
        // One shiny rule among plain ones refuses the whole batch: a partial apply is worse than none.
        var batch = new List<Monster>
        {
            new() { PokemonId = 25 },
            new() { PokemonId = 26, Personal = 1 },
        };

        var ex = await Assert.ThrowsAsync<AlarmValidationException>(
            () => this.Monsters(supported: false).BulkCreateAsync("u1", batch));

        Assert.Equal(Refusal, ex.Message);
        this.VerifyNothingWritten();
    }

    [Fact]
    public async Task UpdateRefusesPersonalWithoutServerSupport()
    {
        var ex = await Assert.ThrowsAsync<AlarmValidationException>(
            () => this.Monsters(supported: false).UpdateAsync("u1", new Monster { Uid = 4, PokemonId = 25, Personal = 3 }));

        Assert.Equal(Refusal, ex.Message);
        this.VerifyNothingWritten();
    }

    [Fact]
    public async Task CreateAcceptsZeroPersonalWithoutServerSupport()
    {
        // The legitimate case: every rule that predates the field, and every rule that does not use it.
        await this.Monsters(supported: false).CreateAsync("u1", new Monster { PokemonId = 25, Personal = 0 });
        await this.Monsters(supported: false).BulkCreateAsync("u1", [new Monster { PokemonId = 26 }]);
        await this.Monsters(supported: false).UpdateAsync("u1", new Monster { Uid = 4, PokemonId = 25 });

        this._proxy.Verify(
            p => p.CreateAsync("pokemon", "u1", It.IsAny<JsonElement>()), Times.Exactly(2));
        this._proxy.Verify(
            p => p.UpdateByUidAsync("pokemon", "u1", 4, It.IsAny<JsonElement>()), Times.Once);
    }

    [Fact]
    public async Task CreateAcceptsPersonalWithServerSupport()
    {
        var created = await this.Monsters(supported: true).CreateAsync("u1", new Monster { PokemonId = 25, Personal = 1 });

        Assert.Equal(9, created.Uid);
        this._proxy.Verify(p => p.CreateAsync("pokemon", "u1", It.IsAny<JsonElement>()), Times.Once);
    }

    // --- Profile import -----------------------------------------------------------------------------

    [Fact]
    public async Task ImportRefusesPersonalWithoutServerSupport()
    {
        var alarms = Json("""{"pokemon":[{"pokemon_id":25},{"pokemon_id":26,"personal":1}]}""");

        var ex = await Assert.ThrowsAsync<AlarmValidationException>(
            () => this.Overview(supported: false).ImportAlarmsAsync("u1", 5, alarms));

        Assert.Contains(Refusal, ex.Message);

        // Before any state moves: no profile switch, no alarm written.
        this._humanProxy.Verify(h => h.SwitchProfileAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        this.VerifyNothingWritten();
    }

    [Fact]
    public async Task ImportAcceptsZeroPersonal()
    {
        var alarms = Json("""{"pokemon":[{"pokemon_id":25,"personal":0},{"pokemon_id":26}]}""");

        var created = await this.Overview(supported: false).ImportAlarmsAsync("u1", 5, alarms);

        Assert.Equal(2, created);
    }

    [Fact]
    public async Task ImportAcceptsPersonalWithServerSupport()
    {
        var alarms = Json("""{"pokemon":[{"pokemon_id":26,"personal":1}]}""");

        var created = await this.Overview(supported: true).ImportAlarmsAsync("u1", 5, alarms);

        Assert.Equal(1, created);
    }

    [Fact]
    public async Task ImportIgnoresPersonalOnTypesThatDoNotCarryIt()
    {
        // Only pokemon rules have the field; a stray key on a raid is not this guard's business.
        var alarms = Json("""{"raid":[{"pokemon_id":150,"level":9000,"personal":1}]}""");

        var created = await this.Overview(supported: false).ImportAlarmsAsync("u1", 5, alarms);

        Assert.Equal(1, created);
    }

    // --- The merge guard ----------------------------------------------------------------------------
    //
    // `personal` is untagged upstream, so it is identity: a plain rule and its shiny twin are two rules.
    // The guard needed no change, and these pin that it stays that way.

    [Fact]
    public async Task APlainRuleAndItsShinyTwinCoexist()
    {
        this.Store("""{"uid":50,"pokemon_id":25,"form":0,"min_iv":0,"distance":0,"personal":0}""");

        await TrackingUpdateReconciler.EnsureNoMergeIntoAnotherAlarmAsync(
            this._proxy.Object, "pokemon", "u1", 0,
            Json("""{"pokemon_id":25,"form":0,"min_iv":0,"distance":0,"personal":1}"""));
    }

    [Fact]
    public async Task AShinyRuleDifferingOnlyInDistanceIsStillATakeover()
    {
        this.Store("""{"uid":50,"pokemon_id":25,"form":0,"min_iv":0,"distance":0,"personal":1}""");

        await Assert.ThrowsAsync<TrackingConflictException>(
            () => TrackingUpdateReconciler.EnsureNoMergeIntoAnotherAlarmAsync(
                this._proxy.Object, "pokemon", "u1", 0,
                Json("""{"pokemon_id":25,"form":0,"min_iv":0,"distance":500,"personal":1}""")));
    }

    [Fact]
    public async Task AStoredRowWithoutPersonalIsNotADifference()
    {
        // A stock PoracleNG row has no `personal` key. A submission carrying 0 must compare as it did
        // before the field existed: identical is a duplicate (no refusal), and one updatable field
        // apart is still the takeover it always was, which proves the missing key was skipped rather
        // than counted as an identity difference.
        this.Store("""{"uid":50,"pokemon_id":25,"form":0,"min_iv":0,"distance":0}""");

        await TrackingUpdateReconciler.EnsureNoMergeIntoAnotherAlarmAsync(
            this._proxy.Object, "pokemon", "u1", 0,
            Json("""{"pokemon_id":25,"form":0,"min_iv":0,"distance":0,"personal":0}"""));

        await Assert.ThrowsAsync<TrackingConflictException>(
            () => TrackingUpdateReconciler.EnsureNoMergeIntoAnotherAlarmAsync(
                this._proxy.Object, "pokemon", "u1", 0,
                Json("""{"pokemon_id":25,"form":0,"min_iv":0,"distance":500,"personal":0}""")));
    }

    // --- helpers ------------------------------------------------------------------------------------

    private MonsterService Monsters(bool supported) =>
        new(this._proxy.Object, this._featureGate.Object, this._remapper.Object,
            CostumeCapabilityDoubles.Supported(), Schema(supported));

    private ProfileOverviewService Overview(bool supported) =>
        new(this._proxy.Object, this._humanProxy.Object, this._featureGate.Object, Schema(supported));

    private static IPoracleV2SchemaService Schema(bool personalTracking) =>
        PoracleTrackingProxyTests.V2Schema(new PoracleV2Capabilities { PersonalTracking = personalTracking });

    private void Store(string row) =>
        this._proxy.Setup(p => p.GetByUserAsync("pokemon", "u1")).ReturnsAsync(Json($"[{row}]"));

    private void VerifyNothingWritten()
    {
        this._proxy.Verify(
            p => p.CreateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<JsonElement>()), Times.Never);
        this._proxy.Verify(
            p => p.UpdateByUidAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<JsonElement>()),
            Times.Never);
    }

    private static JsonElement Json(string text)
    {
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.Clone();
    }
}
