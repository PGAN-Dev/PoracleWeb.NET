using System.Text.Json;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Services.TestAlerts;

namespace Pgan.PoracleWebNet.Tests.Services.TestAlerts;

public class RaidOrEggTestPayloadBuilderTests
{
    private readonly RaidOrEggTestPayloadBuilder _sut = new();

    [Theory]
    [InlineData("raid")]
    [InlineData("egg")]
    public void CanBuildHandlesBothRaidAndEgg(string alarmType) => Assert.True(this._sut.CanBuild(alarmType));

    [Fact]
    public async Task RaidBuildAsyncPreservesPokemonIdAndLevelFromFilter()
    {
        var ctx = Ctx("raid", new
        {
            pokemon_id = 250,
            level = 5
        });
        var result = await this._sut.BuildAsync(ctx);

        Assert.Equal("raid", result.WireType);
        Assert.Equal(250, (int)result.Webhook["pokemon_id"]);
        Assert.Equal(5, (int)result.Webhook["level"]);
    }

    [Fact]
    public async Task EggBuildAsyncSetsPokemonIdToZero()
    {
        var ctx = Ctx("egg", new
        {
            level = 5
        });
        var result = await this._sut.BuildAsync(ctx);

        Assert.Equal("raid", result.WireType);
        Assert.Equal(0, (int)result.Webhook["pokemon_id"]);
        Assert.Equal(0, (int)result.Webhook["cp"]);
    }

    [Fact]
    public async Task BuildAsyncUsesNameFieldNotGymName()
    {
        // Live PGAN DTS reads {{gymName}} enriched from the "name" raw field.
        var ctx = Ctx("raid", new
        {
            pokemon_id = 150
        });
        var result = await this._sut.BuildAsync(ctx);

        Assert.True(result.Webhook.ContainsKey("name"));
        Assert.True(result.Webhook.ContainsKey("url"));
        Assert.False(result.Webhook.ContainsKey("gym_name"));
    }

    [Fact]
    public async Task BuildAsyncTeamFilterIsHonored()
    {
        var ctx = Ctx("raid", new
        {
            pokemon_id = 150,
            team = 1
        });
        var result = await this._sut.BuildAsync(ctx);

        Assert.Equal(1, (int)result.Webhook["team_id"]);
    }

    private static TestPayloadContext Ctx(string alarmType, object alarm) =>
        new(alarmType, TestJsonHelpers.ToJson(alarm), 40.0, -74.0, DateTimeOffset.UtcNow);
}

public class QuestTestPayloadBuilderTests
{
    private readonly QuestTestPayloadBuilder _sut = new();

    [Fact]
    public async Task BuildAsyncStardustRewardProducesStardustInfo()
    {
        var ctx = Ctx(new
        {
            reward_type = 3,
            reward = 2000
        });
        var result = await this._sut.BuildAsync(ctx);

        Assert.Equal("quest", result.WireType);
        var rewards = (object[])result.Webhook["rewards"];
        var first = (Dictionary<string, object>)rewards[0];
        Assert.Equal(3, (int)first["type"]);
        var info = (Dictionary<string, object>)first["info"];
        Assert.Equal(2000, (int)info["amount"]);
    }

    [Fact]
    public async Task BuildAsyncItemRewardProducesItemInfo()
    {
        var ctx = Ctx(new
        {
            reward_type = 2,
            reward = 103
        });
        var result = await this._sut.BuildAsync(ctx);

        var rewards = (object[])result.Webhook["rewards"];
        var first = (Dictionary<string, object>)rewards[0];
        Assert.Equal(2, (int)first["type"]);
        var info = (Dictionary<string, object>)first["info"];
        Assert.Equal(103, (int)info["item_id"]);
    }

    [Fact]
    public async Task BuildAsyncPokemonRewardProducesPokemonInfo()
    {
        var ctx = Ctx(new
        {
            reward_type = 7,
            reward = 133
        });
        var result = await this._sut.BuildAsync(ctx);

        var rewards = (object[])result.Webhook["rewards"];
        var first = (Dictionary<string, object>)rewards[0];
        Assert.Equal(7, (int)first["type"]);
        var info = (Dictionary<string, object>)first["info"];
        Assert.Equal(133, (int)info["pokemon_id"]);
    }

    [Fact]
    public async Task BuildAsyncTemplateIsInGameQuestKeyNotDtsId()
    {
        // Quest webhooks reserve the "template" field for the in-game quest template key
        // (e.g. CHALLENGE_BASE_SPIN_S_ITEM). Our DTS template id must NOT collide with it.
        var ctx = Ctx(new
        {
            reward_type = 3,
            reward = 500
        });
        var result = await this._sut.BuildAsync(ctx);

        var template = (string)result.Webhook["template"];
        Assert.StartsWith("CHALLENGE_", template, StringComparison.Ordinal);
    }

    private static TestPayloadContext Ctx(object alarm) =>
        new("quest", TestJsonHelpers.ToJson(alarm), 40.0, -74.0, DateTimeOffset.UtcNow);
}

public class PokestopTestPayloadBuilderTests
{
    private readonly PokestopTestPayloadBuilder _sut = new();

    [Theory]
    [InlineData("invasion")]
    [InlineData("lure")]
    public void CanBuildHandlesInvasionAndLure(string alarmType) => Assert.True(this._sut.CanBuild(alarmType));

    [Fact]
    public async Task InvasionBuildAsyncProducesIncidentFields()
    {
        var ctx = new TestPayloadContext("invasion", TestJsonHelpers.ToJson(new
        {
            grunt_type = 41
        }), 40.0, -74.0, DateTimeOffset.UtcNow);
        var result = await this._sut.BuildAsync(ctx);

        Assert.Equal("pokestop", result.WireType);
        // Live PoracleNG invasion controller expects "incident_grunt_type" + "incident_expiration",
        // not "grunt_type" + "incident_expire_timestamp".
        Assert.True(result.Webhook.ContainsKey("incident_grunt_type"));
        Assert.True(result.Webhook.ContainsKey("incident_expiration"));
        Assert.False(result.Webhook.ContainsKey("grunt_type"));
        // Pokestop name lives on the "name" field, not "pokestop_name".
        Assert.True(result.Webhook.ContainsKey("name"));
        Assert.False(result.Webhook.ContainsKey("pokestop_name"));
    }

    [Fact]
    public async Task LureBuildAsyncProducesLureFields()
    {
        var ctx = new TestPayloadContext("lure", TestJsonHelpers.ToJson(new
        {
            lure_id = 506
        }), 40.0, -74.0, DateTimeOffset.UtcNow);
        var result = await this._sut.BuildAsync(ctx);

        Assert.Equal("pokestop", result.WireType);
        Assert.Equal(506, (int)result.Webhook["lure_id"]);
        Assert.True(result.Webhook.ContainsKey("lure_expiration"));
    }
}

public class NestTestPayloadBuilderTests
{
    private readonly NestTestPayloadBuilder _sut = new();

    [Fact]
    public void CanBuildClaimsNest() => Assert.True(this._sut.CanBuild("nest"));

    [Fact]
    public async Task BuildAsyncThrowsNotSupportedBecauseUpstreamHasNoNestTestSurface()
    {
        // Nest test alerts are not supported — the builder claims the type (so the
        // dispatcher matches) but throws a specific NotSupportedException the controller
        // translates to HTTP 501. Regression guard against accidentally reviving the
        // best-effort path.
        var ctx = new TestPayloadContext(
            "nest",
            TestJsonHelpers.ToJson(new
            {
                pokemon_id = 246
            }),
            40.0,
            -74.0,
            DateTimeOffset.UtcNow);

        await Assert.ThrowsAsync<NotSupportedException>(() => this._sut.BuildAsync(ctx));
    }
}

public class GymTestPayloadBuilderTests
{
    private readonly GymTestPayloadBuilder _sut = new();

    [Fact]
    public async Task BuildAsyncHonorsTeamFilter()
    {
        var ctx = new TestPayloadContext("gym", TestJsonHelpers.ToJson(new
        {
            team = 3
        }), 40.0, -74.0, DateTimeOffset.UtcNow);
        var result = await this._sut.BuildAsync(ctx);

        Assert.Equal("gym", result.WireType);
        Assert.Equal(3, (int)result.Webhook["team_id"]);
        // old_team must differ so the transition renders.
        Assert.NotEqual(3, (int)result.Webhook["old_team_id"]);
    }
}

internal static class TestJsonHelpers
{
    private static readonly JsonSerializerOptions SnakeCase = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public static JsonElement ToJson(object value)
    {
        var json = JsonSerializer.Serialize(value, SnakeCase);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
