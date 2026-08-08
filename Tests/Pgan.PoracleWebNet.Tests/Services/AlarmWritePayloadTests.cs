using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// What actually goes on the wire for an alarm write.
/// <para>
/// Alarm writes carried <c>profile_no</c> stamped from the caller's JWT claim. That claim goes stale
/// whenever <c>current_profile_no</c> moves out of band — the active-hours scheduler, the bot's
/// <c>!profile</c> command, a second tab — and JWTs live four hours. PoracleNG takes a submitted
/// <c>profile_no</c> at face value for the pokemon type (verified: <c>profile_no: 9</c> creates a row on
/// a profile that does not exist) while scoping every read to the live one. So a stale claim wrote an
/// alarm that returned 201 and was then invisible to reads and undeletable. Omitting it makes PoracleNG
/// use <c>current_profile_no</c>, which is what the other nine types already did. See #411.
/// </para>
/// </summary>
public class AlarmWritePayloadTests
{
    private readonly Mock<IPoracleTrackingProxy> _proxy = new();
    private readonly Mock<IFeatureGate> _featureGate = new();
    private readonly List<JsonElement> _sent = [];

    public AlarmWritePayloadTests()
    {
        this._featureGate.Setup(g => g.EnsureEnabledAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        this._proxy
            .Setup(p => p.CreateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<JsonElement>()))
            .Callback<string, string, JsonElement>((_, _, body) => this._sent.Add(body.Clone()))
            .ReturnsAsync(new TrackingCreateResult([1], 0, 0, 1));
    }

    private MonsterService Monsters() => new(this._proxy.Object, this._featureGate.Object);

    private static IEnumerable<JsonElement> Objects(JsonElement sent) =>
        sent.ValueKind == JsonValueKind.Array ? sent.EnumerateArray() : [sent];

    [Fact]
    public async Task CreateDoesNotSendProfileNo()
    {
        await Monsters().CreateAsync("u1", new Monster { PokemonId = 201, ProfileNo = 3 });

        Assert.All(Objects(this._sent[0]), o => Assert.False(o.TryGetProperty("profile_no", out _)));
    }

    [Fact]
    public async Task CreateDoesNotSendProfileNoWhenItIsZero()
    {
        // Zero is a real profile number, so "strip only non-zero" would still target the wrong profile.
        await Monsters().CreateAsync("u1", new Monster { PokemonId = 201, ProfileNo = 0 });

        Assert.All(Objects(this._sent[0]), o => Assert.False(o.TryGetProperty("profile_no", out _)));
    }

    [Fact]
    public async Task UpdateDoesNotSendProfileNo()
    {
        await Monsters().UpdateAsync("u1", new Monster { Uid = 9, PokemonId = 201, ProfileNo = 3 });

        Assert.All(Objects(this._sent[0]), o => Assert.False(o.TryGetProperty("profile_no", out _)));
    }

    [Fact]
    public async Task BulkCreateStripsProfileNoFromEveryElement()
    {
        await Monsters().BulkCreateAsync("u1",
        [
            new Monster { PokemonId = 1, ProfileNo = 2 },
            new Monster { PokemonId = 2, ProfileNo = 3 }
        ]);

        var items = this._sent[0].EnumerateArray().ToList();
        Assert.Equal(2, items.Count);
        Assert.All(items, o => Assert.False(o.TryGetProperty("profile_no", out _)));
    }

    [Fact]
    public async Task CreateStillOmitsTheDefaultUidSoItIsAnInsert()
    {
        await Monsters().CreateAsync("u1", new Monster { PokemonId = 201 });

        Assert.All(Objects(this._sent[0]), o => Assert.False(o.TryGetProperty("uid", out _)));
    }

    [Fact]
    public async Task UpdateStillSendsItsUidSoItTargetsTheRightRow()
    {
        await Monsters().UpdateAsync("u1", new Monster { Uid = 42, PokemonId = 201 });

        Assert.All(Objects(this._sent[0]), o => Assert.Equal(42, o.GetProperty("uid").GetInt32()));
    }

    [Fact]
    public async Task TheRestOfThePayloadIsUntouched()
    {
        await Monsters().CreateAsync("u1", new Monster { PokemonId = 149, Distance = 500, ProfileNo = 7 });

        var o = Objects(this._sent[0]).First();
        Assert.Equal(149, o.GetProperty("pokemon_id").GetInt32());
        Assert.Equal(500, o.GetProperty("distance").GetInt32());
        Assert.Equal("u1", o.GetProperty("id").GetString());
    }
}
