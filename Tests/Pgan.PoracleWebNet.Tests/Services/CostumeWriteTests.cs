using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// PoracleNG 5.4.0 stores a costume on every pokemon and raid rule, so the alarm services write one
/// without asking the server whether it can. Both services are built from the proxy, the feature gate
/// and the uid remapper alone; a costume capability dependency cannot come back without these failing
/// to compile.
/// </summary>
public class CostumeWriteTests
{
    private const int Halloween = 85;

    private readonly Mock<IPoracleTrackingProxy> _proxy = new();
    private readonly Mock<IFeatureGate> _gate = new();
    private readonly Mock<ITrackedUidRemapper> _remapper = new();

    public CostumeWriteTests()
    {
        this._gate.Setup(g => g.EnsureEnabledAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        this._proxy.Setup(p => p.GetByUserAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(JsonDocument.Parse("[]").RootElement.Clone());
        this._proxy.Setup(p => p.CreateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([1], 0, 0, 1));
    }

    private MonsterService Monsters() => new(this._proxy.Object, this._gate.Object, this._remapper.Object);

    private RaidService Raids() =>
        new(this._proxy.Object, this._gate.Object, NullLogger<RaidService>.Instance, this._remapper.Object);

    // A single create posts one object; a bulk create posts an array of them.
    private static IEnumerable<int> SentCostumes(IInvocation call)
    {
        var body = (JsonElement)call.Arguments[2];
        return body.ValueKind == JsonValueKind.Array
            ? body.EnumerateArray().Select(e => e.GetProperty("costume").GetInt32()).ToList()
            : [body.GetProperty("costume").GetInt32()];
    }

    private IEnumerable<int> CreatedCostumes(string type) => this._proxy.Invocations
        .Where(i => i.Method.Name == nameof(IPoracleTrackingProxy.CreateAsync) && (string)i.Arguments[0] == type)
        .SelectMany(SentCostumes);

    [Fact]
    public async Task PokemonWithACostumeIsWritten()
    {
        await this.Monsters().CreateAsync("u1", new Monster { PokemonId = 25, Costume = Halloween });

        Assert.Equal([Halloween], this.CreatedCostumes("pokemon"));
    }

    [Fact]
    public async Task RaidWithACostumeIsWritten()
    {
        await this.Raids().CreateAsync("u1", new Raid { Level = 5, PokemonId = 25, Costume = Halloween });

        Assert.Equal([Halloween], this.CreatedCostumes("raid"));
    }

    [Fact]
    public async Task BulkCreateWritesEveryCostumeUnchanged()
    {
        await this.Monsters().BulkCreateAsync("u1", [new Monster { PokemonId = 25, Costume = Halloween }, new Monster { PokemonId = 26, Costume = 0 }]);
        await this.Raids().BulkCreateAsync("u1", [new Raid { Level = 5, PokemonId = 25, Costume = Halloween }]);

        Assert.Equal([Halloween, 0], this.CreatedCostumes("pokemon"));
        Assert.Equal([Halloween], this.CreatedCostumes("raid"));
    }

    [Fact]
    public async Task OrdinaryAlarmsStillCarryTheAnyCostumeWildcard()
    {
        await this.Monsters().CreateAsync("u1", new Monster { PokemonId = 25 });
        await this.Raids().CreateAsync("u1", new Raid { Level = 5, PokemonId = 25 });

        Assert.Equal([9000], this.CreatedCostumes("pokemon"));
        Assert.Equal([9000], this.CreatedCostumes("raid"));
    }
}
