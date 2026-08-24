using System.Text.Json;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// PoracleNG renders a sentence for every tracking rule and hands it back on every v1 per-type read,
/// with no query parameter asked for -- verified live against 5.1.0 and 5.2.1. It travels one way only:
/// no tracking table has a <c>description</c> column, so the field must reach the models on the way in
/// and must never reach PoracleNG on the way out. See #810.
/// </summary>
public class AlarmDescriptionPassthroughTests
{
    private static readonly JsonSerializerOptions SnakeCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private const string LiveDescription =
        "**Bulbasaur**  | distance: 5000m | iv: 90%-100% | cp: 1200-4000 | level: 20-35";

    private readonly Mock<IPoracleTrackingProxy> _proxy = new();
    private readonly Mock<IFeatureGate> _featureGate = new();
    private readonly Mock<ITrackedUidRemapper> _remapper = new();
    private readonly MonsterService _monsters;

    public AlarmDescriptionPassthroughTests()
    {
        this._featureGate.Setup(g => g.EnsureEnabledAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        this._monsters = new MonsterService(this._proxy.Object, this._featureGate.Object, this._remapper.Object, CostumeCapabilityDoubles.Supported());
    }

    private static JsonElement StoredRow(int uid, string? description) => JsonSerializer.SerializeToElement(
        new[]
        {
            new
            {
                uid,
                id = "user1",
                pokemon_id = 25,
                distance = 5000,
                min_iv = 90,
                max_iv = 100,
                template = "1",
                description,
            },
        },
        SnakeCase);

    /// <summary>Captures the body the service posts, so the tests can read the wire shape.</summary>
    private JsonElement[] CaptureWrites()
    {
        var slot = new JsonElement[1];
        this._proxy
            .Setup(p => p.CreateAsync("pokemon", "user1", It.IsAny<JsonElement>()))
            .Callback<string, string, JsonElement>((_, _, body) => slot[0] = body.Clone())
            .ReturnsAsync(new TrackingCreateResult([], 0, 0, 0));
        return slot;
    }

    /// <summary>
    /// Edits go through UpdateByUidAsync, not CreateAsync -- #805 moved the pokemon update path onto
    /// PoracleNG's uid-addressed v2 PUT, and the proxy chooses v1 or v2 underneath. Capturing the create
    /// call here would observe nothing and assert nothing.
    /// </summary>
    private JsonElement[] CaptureUpdates()
    {
        var slot = new JsonElement[1];
        this._proxy
            .Setup(p => p.UpdateByUidAsync("pokemon", "user1", It.IsAny<int>(), It.IsAny<JsonElement>()))
            .Callback<string, string, int, JsonElement>((_, _, _, body) => slot[0] = body.Clone())
            .ReturnsAsync(new TrackingUpdateResult(7, true));
        return slot;
    }

    [Fact]
    public async Task ReadCarriesTheDescriptionOntoTheModel()
    {
        this._proxy.Setup(p => p.GetByUserAsync("pokemon", "user1")).ReturnsAsync(StoredRow(1, LiveDescription));

        var monster = Assert.Single(await this._monsters.GetByUserAsync("user1", 1));

        Assert.Equal(LiveDescription, monster.Description);
    }

    [Fact]
    public async Task AnOlderPoracleThatSendsNoDescriptionLeavesItNull()
    {
        // The degradation path: no field, no exception, the rest of the row still deserializes.
        this._proxy.Setup(p => p.GetByUserAsync("pokemon", "user1")).ReturnsAsync(
            JsonSerializer.SerializeToElement(new[] { new { uid = 1, id = "user1", pokemon_id = 25 } }, SnakeCase));

        var monster = Assert.Single(await this._monsters.GetByUserAsync("user1", 1));

        Assert.Null(monster.Description);
        Assert.Equal(25, monster.PokemonId);
    }

    [Fact]
    public async Task CreateDoesNotSendTheDescription()
    {
        this._proxy.Setup(p => p.GetByUserAsync("pokemon", "user1")).ReturnsAsync(JsonDocument.Parse("[]").RootElement);
        var written = this.CaptureWrites();

        await this._monsters.CreateAsync(
            "user1",
            new Monster { PokemonId = 25, Distance = 5000, Description = LiveDescription });

        Assert.False(written[0].TryGetProperty("description", out _));
    }

    [Fact]
    public async Task CreateStillSendsEveryOtherProperty()
    {
        // The legitimate-case-still-passes half. Stripping one name must not disturb the rest of the body:
        // an assertion that only says "description is gone" would not notice a strip that takes too much.
        this._proxy.Setup(p => p.GetByUserAsync("pokemon", "user1")).ReturnsAsync(JsonDocument.Parse("[]").RootElement);
        var written = this.CaptureWrites();

        var model = new Monster
        {
            PokemonId = 25,
            Distance = 5000,
            MinIv = 90,
            Clean = 1,
            Template = "1",
            Description = LiveDescription,
        };

        await this._monsters.CreateAsync("user1", model);

        var expected = JsonSerializer.SerializeToElement(model, SnakeCase)
            .EnumerateObject()
            .Select(p => p.Name)
            .Where(n => n is not ("description" or "profile_no" or "uid"))
            .ToList();

        Assert.NotEmpty(expected);
        foreach (var name in expected)
        {
            Assert.True(written[0].TryGetProperty(name, out _), $"body lost {name}");
        }
    }

    [Fact]
    public async Task AnEditDoesNotEchoTheStoredDescriptionBack()
    {
        // TrackingFieldPreserver copies every stored property the model does not state (#730), so the
        // description was going back out on every edit even before the models carried it.
        this._proxy.Setup(p => p.GetByUserAsync("pokemon", "user1")).ReturnsAsync(StoredRow(7, LiveDescription));
        var written = this.CaptureUpdates();

        await this._monsters.UpdateAsync(
            "user1",
            new Monster { Uid = 7, Id = "user1", PokemonId = 25, Distance = 6000 });

        Assert.False(written[0].TryGetProperty("description", out _));
    }

    [Fact]
    public async Task BulkDistanceRewriteDropsTheDescriptionAndKeepsTheRest()
    {
        this._proxy.Setup(p => p.GetByUserAsync("pokemon", "user1")).ReturnsAsync(StoredRow(7, LiveDescription));
        var written = this.CaptureWrites();

        await this._monsters.UpdateDistanceByUserAsync("user1", 1, 1234);

        var row = written[0].EnumerateArray().Single();
        Assert.False(row.TryGetProperty("description", out _));
        Assert.Equal(25, row.GetProperty("pokemon_id").GetInt32());
        Assert.Equal(90, row.GetProperty("min_iv").GetInt32());
        Assert.Equal(1234, row.GetProperty("distance").GetInt32());
    }

    [Fact]
    public async Task TheCollisionGuardIsBlindToTheDescription()
    {
        // The reconciler compares submitted against stored field by field. A description present on one
        // side only would read as a difference -- either refusing a legitimate edit (#553) or hiding a
        // real collision (#561). It is listed in AssignedByPoracle/IgnoredForNoOp; this pins that.
        this._proxy.Setup(p => p.GetByUserAsync("pokemon", "user1")).ReturnsAsync(StoredRow(7, LiveDescription));
        this._proxy
            .Setup(p => p.CreateAsync("pokemon", "user1", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([], 0, 0, 0));

        // Same row, same values: a no-op edit, which must be allowed rather than read as a collision.
        var exception = await Record.ExceptionAsync(() => this._monsters.UpdateAsync(
            "user1",
            new Monster { Uid = 7, Id = "user1", PokemonId = 25, Distance = 5000, MinIv = 90, Template = "1" }));

        Assert.Null(exception);
    }
}
