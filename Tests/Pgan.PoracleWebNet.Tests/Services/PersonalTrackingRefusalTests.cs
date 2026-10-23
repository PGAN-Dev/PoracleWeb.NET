using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
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

        // Import writes one v2 create per type onto the target profile; count every row as inserted.
        this._proxy
            .Setup(p => p.CreateOnProfileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<JsonElement>(), It.IsAny<int>()))
            .ReturnsAsync((string _, string _, JsonElement body, int _) =>
                new TrackingCreateResult([], 0, 0, body.ValueKind == JsonValueKind.Array ? body.GetArrayLength() : 1));
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

        // Before any state moves: no alarm written.
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

    // --- Constructed without a schema (main's own call sites do) -----------------------------------

    [Fact]
    public async Task WithoutASchemaPersonalIsRefusedAndZeroPersonalIsAccepted()
    {
        // A caller that predates the schema dependency gets no capabilities, which fails closed.
        var monsters = new MonsterService(this._proxy.Object, this._featureGate.Object, this._remapper.Object,
            NullLogger<MonsterService>.Instance);

        var ex = await Assert.ThrowsAsync<AlarmValidationException>(
            () => monsters.CreateAsync("u1", new Monster { PokemonId = 25, Personal = 1 }));
        Assert.Equal(Refusal, ex.Message);
        this.VerifyNothingWritten();

        await monsters.CreateAsync("u1", new Monster { PokemonId = 25, Personal = 0 });
        this._proxy.Verify(
            p => p.CreateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<JsonElement>()), Times.Once);
    }

    // The client-side merge guard (TrackingUpdateReconciler) that pinned `personal` as identity is gone
    // upstream: 5.4.0 classifies a create with a dry run and PoracleNG decides. Its three tests went with it.

    // --- helpers ------------------------------------------------------------------------------------

    private MonsterService Monsters(bool supported) =>
        new(this._proxy.Object, this._featureGate.Object, this._remapper.Object,
            NullLogger<MonsterService>.Instance, Schema(supported));

    private ProfileOverviewService Overview(bool supported) =>
        new(this._proxy.Object, this._humanProxy.Object, this._featureGate.Object,
            Mock.Of<IPoracleIncidentProxy>(), Schema(supported));

    private static IPoracleV2SchemaService Schema(bool personalTracking) =>
        PoracleTrackingProxyTests.V2Schema(new PoracleV2Capabilities { PersonalTracking = personalTracking });

    private void VerifyNothingWritten()
    {
        this._proxy.Verify(
            p => p.CreateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<JsonElement>()), Times.Never);
        this._proxy.Verify(
            p => p.UpdateByUidAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<JsonElement>()),
            Times.Never);
        this._proxy.Verify(
            p => p.CreateOnProfileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<JsonElement>(), It.IsAny<int>()),
            Times.Never);
    }

    private static JsonElement Json(string text)
    {
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.Clone();
    }
}
