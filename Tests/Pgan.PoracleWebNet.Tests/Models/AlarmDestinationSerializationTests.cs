using System.Text.Json;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Models;

public class AlarmDestinationSerializationTests
{
    [Fact]
    public void UnlinkedMonster_SerialisesWithoutDestination()
    {
        var json = JsonSerializer.Serialize(new Monster { Uid = 4, PokemonId = 25 }, JsonSerializerOptions.Web);

        Assert.DoesNotContain("destination", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LinkedMonster_SerialisesItsDestination()
    {
        var json = JsonSerializer.Serialize(new Monster { Uid = 4, Destination = "telegram", PausedDestination = true }, JsonSerializerOptions.Web);

        Assert.Contains("\"destination\":\"telegram\"", json);
        Assert.Contains("\"pausedDestination\":true", json);
    }

    [Fact]
    public void Destination_NeverReachesPoracleNg()
    {
        var body = PoracleJsonHelper.SerializeToElement(new Monster { Uid = 4, PokemonId = 25, Destination = "telegram", PausedDestination = true });

        Assert.False(body.TryGetProperty("destination", out _));
        Assert.False(body.TryGetProperty("paused_destination", out _));
        Assert.True(body.TryGetProperty("pokemon_id", out _));
    }
}
