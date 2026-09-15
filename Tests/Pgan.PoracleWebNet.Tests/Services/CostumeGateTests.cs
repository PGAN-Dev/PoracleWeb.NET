using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// The costume gate where it is enforced: the alarm services, which quick-pick apply and profile import
/// reach without ever passing a dialog.
/// </summary>
/// <remarks>
/// Hiding the control in the SPA is decoration on its own — the endpoints stay reachable by direct call
/// and the capability the SPA holds is client state. These tests are the enforcement.
/// </remarks>
public class CostumeGateTests
{
    private readonly Mock<IPoracleTrackingProxy> _proxy = new();
    private readonly Mock<IFeatureGate> _gate = new();
    private readonly Mock<ITrackedUidRemapper> _remapper = new();

    public CostumeGateTests()
    {
        this._gate.Setup(g => g.EnsureEnabledAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        this._proxy.Setup(p => p.GetByUserAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(EmptyArray());
        this._proxy.Setup(p => p.CreateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([1], 0, 0, 1));
    }

    private MonsterService Monsters(ICostumeCapabilityService costumes) =>
        new(this._proxy.Object, this._gate.Object, this._remapper.Object, costumes);

    private RaidService Raids(ICostumeCapabilityService costumes) =>
        new(this._proxy.Object, this._gate.Object, NullLogger<RaidService>.Instance, this._remapper.Object, costumes);

    private static JsonElement EmptyArray() => JsonDocument.Parse("[]").RootElement.Clone();

    private void AssertNothingWritten() => this._proxy.Verify(
        p => p.CreateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<JsonElement>()), Times.Never);

    // --- Still works on a server that has the columns ---

    [Fact]
    public async Task PokemonCostumeIsWrittenWhenTheServerHasTheColumn()
    {
        await Monsters(CostumeCapabilityDoubles.Supported())
            .CreateAsync("u1", new Monster { PokemonId = 25, Costume = 85 });

        this._proxy.Verify(
            p => p.CreateAsync("pokemon", "u1", It.IsAny<JsonElement>()), Times.Once);
    }

    [Fact]
    public async Task RaidCostumeIsWrittenWhenTheServerHasTheColumn()
    {
        await Raids(CostumeCapabilityDoubles.Supported())
            .CreateAsync("u1", new Raid { Level = 5, PokemonId = 25, Costume = 85 });

        this._proxy.Verify(p => p.CreateAsync("raid", "u1", It.IsAny<JsonElement>()), Times.Once);
    }

    /// <summary>
    /// The case that must not regress: every ordinary alarm carries the 9000 wildcard, so a gate that
    /// refused on the field being present rather than on its value would ban all of them on 5.1.0.
    /// </summary>
    [Fact]
    public async Task OrdinaryAlarmStillSavesOnAServerWithoutTheColumns()
    {
        await Monsters(CostumeCapabilityDoubles.Unsupported())
            .CreateAsync("u1", new Monster { PokemonId = 25 });

        this._proxy.Verify(p => p.CreateAsync("pokemon", "u1", It.IsAny<JsonElement>()), Times.Once);
    }

    [Fact]
    public async Task OrdinaryRaidAlarmStillSavesOnAServerWithoutTheColumns()
    {
        await Raids(CostumeCapabilityDoubles.Unsupported())
            .CreateAsync("u1", new Raid { Level = 5 });

        this._proxy.Verify(p => p.CreateAsync("raid", "u1", It.IsAny<JsonElement>()), Times.Once);
    }

    // --- Refused on a server that does not ---

    [Fact]
    public async Task CreatingAPokemonCostumeRuleIsRefusedOnAnOldServer()
    {
        await Assert.ThrowsAsync<AlarmValidationException>(() => Monsters(CostumeCapabilityDoubles.Unsupported())
            .CreateAsync("u1", new Monster { PokemonId = 25, Costume = 85 }));

        this.AssertNothingWritten();
    }

    [Fact]
    public async Task EditingAPokemonRuleOntoACostumeIsRefusedOnAnOldServer()
    {
        await Assert.ThrowsAsync<AlarmValidationException>(() => Monsters(CostumeCapabilityDoubles.Unsupported())
            .UpdateAsync("u1", new Monster { Uid = 7, PokemonId = 25, Costume = 85 }));

        this.AssertNothingWritten();
    }

    [Fact]
    public async Task CreatingARaidCostumeRuleIsRefusedOnAnOldServer()
    {
        await Assert.ThrowsAsync<AlarmValidationException>(() => Raids(CostumeCapabilityDoubles.Unsupported())
            .CreateAsync("u1", new Raid { Level = 5, PokemonId = 25, Costume = 85 }));

        this.AssertNothingWritten();
    }

    [Fact]
    public async Task EditingARaidRuleOntoACostumeIsRefusedOnAnOldServer()
    {
        await Assert.ThrowsAsync<AlarmValidationException>(() => Raids(CostumeCapabilityDoubles.Unsupported())
            .UpdateAsync("u1", new Raid { Uid = 7, Level = 5, PokemonId = 25, Costume = 85 }));

        this.AssertNothingWritten();
    }

    /// <summary>
    /// Profile import and quick-pick apply come through the bulk path, which no dialog gate covers.
    /// </summary>
    [Fact]
    public async Task BulkCreateIsRefusedWhenOneRuleCarriesACostume()
    {
        await Assert.ThrowsAsync<AlarmValidationException>(() => Monsters(CostumeCapabilityDoubles.Unsupported())
            .BulkCreateAsync("u1",
            [
                new Monster { PokemonId = 25 },
                new Monster { PokemonId = 26, Costume = 85 },
            ]));

        this.AssertNothingWritten();
    }

    [Fact]
    public async Task BulkRaidCreateIsRefusedWhenOneRuleCarriesACostume()
    {
        await Assert.ThrowsAsync<AlarmValidationException>(() => Raids(CostumeCapabilityDoubles.Unsupported())
            .BulkCreateAsync("u1",
            [
                new Raid { Level = 5 },
                new Raid { Level = 5, PokemonId = 25, Costume = 85 },
            ]));

        this.AssertNothingWritten();
    }
}
