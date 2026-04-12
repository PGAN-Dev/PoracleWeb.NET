using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models.Pvp;
using Pgan.PoracleWebNet.Core.Services.Pvp;
using Pgan.PoracleWebNet.Core.Services.TestAlerts;

namespace Pgan.PoracleWebNet.Tests.Services.TestAlerts;

public class PokemonTestPayloadBuilderTests
{
    // Azumarill (#184) base stats — the canonical Great League "bulk over attack" pick.
    private static readonly BaseStats AzumarillStats = new(112, 152, 225);

    private readonly Mock<IMasterDataService> _masterData = new();
    private readonly PokemonTestPayloadBuilder _sut;

    public PokemonTestPayloadBuilderTests()
    {
        var pvpService = new PvpRankService(new MemoryCache(new MemoryCacheOptions()));
        this._sut = new PokemonTestPayloadBuilder(pvpService, this._masterData.Object);
        this._masterData.Setup(m => m.GetBaseStatsAsync(184, It.IsAny<int>())).ReturnsAsync(AzumarillStats);
    }

    [Fact]
    public async Task CanBuildMatchesOnlyPokemon()
    {
        Assert.True(this._sut.CanBuild("pokemon"));
        Assert.False(this._sut.CanBuild("raid"));
        Assert.False(this._sut.CanBuild("egg"));
        Assert.False(this._sut.CanBuild("quest"));
    }

    [Fact]
    public async Task BuildAsyncWireTypeIsPokemon()
    {
        var ctx = Ctx(new
        {
            pokemon_id = 184,
            form = 0
        });
        var result = await this._sut.BuildAsync(ctx);
        Assert.Equal("pokemon", result.WireType);
    }

    [Fact]
    public async Task BuildAsyncGreatLeagueFilterResolvesRankOneCombo()
    {
        // Alarm: Azumarill, Great League (pvp_ranking_league = 1500 CP cap), rank 1-1.
        // Encoding matches the live frontend (pokemon-edit-dialog) and QuickPickService.cs:773.
        var ctx = Ctx(new
        {
            pokemon_id = 184,
            form = 0,
            pvp_ranking_league = 1500,
            pvp_ranking_best = 1,
            pvp_ranking_worst = 1,
        });

        var result = await this._sut.BuildAsync(ctx);

        var atk = (int)result.Webhook["individual_attack"];
        var def = (int)result.Webhook["individual_defense"];
        var sta = (int)result.Webhook["individual_stamina"];
        var cp = (int)result.Webhook["cp"];

        Assert.True(def >= 14, $"expected bulky def for rank 1 GL Azumarill, got {def}");
        Assert.True(sta >= 14, $"expected bulky sta for rank 1 GL Azumarill, got {sta}");
        Assert.True(atk <= 5, $"expected low atk for rank 1 GL Azumarill, got {atk}");
        Assert.InRange(cp, 1480, 1500);

        Assert.True(result.Webhook.ContainsKey("pvp_rankings_great_league"));
        var greatPanel = (List<Dictionary<string, object>>)result.Webhook["pvp_rankings_great_league"];
        Assert.NotEmpty(greatPanel);
        Assert.Equal(1, (int)greatPanel[0]["rank"]);
    }

    [Fact]
    public async Task BuildAsyncUltraLeagueFilterResolvesAgainstUltraCap()
    {
        // Regression guard: an earlier revision mapped pvp_ranking_league via a 1/2/3 ordinal,
        // which silently collapsed every real filter (stored as CP cap) to Great League.
        var ctx = Ctx(new
        {
            pokemon_id = 184,
            form = 0,
            pvp_ranking_league = 2500,
            pvp_ranking_best = 1,
            pvp_ranking_worst = 1,
        });

        var result = await this._sut.BuildAsync(ctx);

        var cp = (int)result.Webhook["cp"];
        Assert.True(cp > 1500, $"UL rank 1 should push CP above the GL cap, got {cp}");
        Assert.True(cp <= 2500, $"UL rank 1 must honor the Ultra cap, got {cp}");

        Assert.True(result.Webhook.ContainsKey("pvp_rankings_ultra_league"));
        var ultraPanel = (List<Dictionary<string, object>>)result.Webhook["pvp_rankings_ultra_league"];
        Assert.NotEmpty(ultraPanel);
        Assert.Equal(1, (int)ultraPanel[0]["rank"]);
    }

    [Fact]
    public async Task BuildAsyncLittleLeagueFilterResolvesAgainstLittleCap()
    {
        // Wooper (#194, 75/75/160) is a canonical Little League pick.
        this._masterData.Setup(m => m.GetBaseStatsAsync(194, It.IsAny<int>())).ReturnsAsync(new BaseStats(75, 75, 160));

        var ctx = Ctx(new
        {
            pokemon_id = 194,
            form = 0,
            pvp_ranking_league = 500,
            pvp_ranking_best = 1,
            pvp_ranking_worst = 1,
        });

        var result = await this._sut.BuildAsync(ctx);

        var cp = (int)result.Webhook["cp"];
        Assert.True(cp <= 500, $"LL rank 1 must honor the 500 CP cap, got {cp}");
        Assert.True(cp >= 480, $"LL rank 1 should hug the cap, got {cp}");
    }

    [Fact]
    public async Task BuildAsyncCombinedIvFilterProducesConsistentBodyAndRankPanel()
    {
        // When a user combines a non-PVP IV floor with species-level synthesis, the
        // resolved IVs must satisfy the %-IV range AND the rank panel must be recomputed
        // from those final IVs (not stale pre-adjustment IVs).
        var ctx = Ctx(new
        {
            pokemon_id = 184,
            form = 0,
            min_iv = 80,
            max_iv = 100,
        });

        var result = await this._sut.BuildAsync(ctx);

        var atk = (int)result.Webhook["individual_attack"];
        var def = (int)result.Webhook["individual_defense"];
        var sta = (int)result.Webhook["individual_stamina"];
        var totalPct = (atk + def + sta) / 45.0 * 100.0;
        Assert.InRange(totalPct, 80.0, 100.0);

        var greatPanel = (List<Dictionary<string, object>>)result.Webhook["pvp_rankings_great_league"];
        Assert.Single(greatPanel);
    }

    [Fact]
    public async Task BuildAsyncIvFloorFilterIsHonored()
    {
        // Require atk>=14, def>=14, sta>=14 → payload must satisfy the floor.
        var ctx = Ctx(new
        {
            pokemon_id = 184,
            form = 0,
            atk = 14,
            def = 14,
            sta = 14,
        });

        var result = await this._sut.BuildAsync(ctx);

        Assert.True((int)result.Webhook["individual_attack"] >= 14);
        Assert.True((int)result.Webhook["individual_defense"] >= 14);
        Assert.True((int)result.Webhook["individual_stamina"] >= 14);
    }

    [Fact]
    public async Task BuildAsyncLevelRangeFilterIsHonored()
    {
        var ctx = Ctx(new
        {
            pokemon_id = 184,
            form = 0,
            min_level = 30,
            max_level = 35,
        });

        var result = await this._sut.BuildAsync(ctx);

        var level = (double)result.Webhook["pokemon_level"];
        Assert.InRange(level, 30.0, 35.0);
    }

    [Fact]
    public async Task BuildAsyncGenderFilterIsHonored()
    {
        var ctx = Ctx(new
        {
            pokemon_id = 184,
            form = 0,
            gender = 2, // female
        });

        var result = await this._sut.BuildAsync(ctx);

        Assert.Equal(2, (int)result.Webhook["gender"]);
    }

    [Fact]
    public async Task BuildAsyncPopulatesUserLocationAsCoordinates()
    {
        var ctx = new TestPayloadContext("pokemon", ToJson(new
        {
            pokemon_id = 184
        }), 51.1, -0.5, DateTimeOffset.UtcNow);

        var result = await this._sut.BuildAsync(ctx);

        Assert.Equal(51.1, (double)result.Webhook["latitude"]);
        Assert.Equal(-0.5, (double)result.Webhook["longitude"]);
    }

    [Fact]
    public async Task BuildAsyncWithNoBaseStatsFallsBackWithoutCrashing()
    {
        // Master data returns null — exercise the no-base-stats path.
        this._masterData.Reset();
        this._masterData.Setup(m => m.GetBaseStatsAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync((BaseStats?)null);

        var ctx = Ctx(new
        {
            pokemon_id = 9999
        });
        var result = await this._sut.BuildAsync(ctx);

        Assert.Equal("pokemon", result.WireType);
        Assert.True(result.Webhook.ContainsKey("pokemon_id"));
        // PVP panels should NOT be emitted when we have no base stats to rank against.
        Assert.False(result.Webhook.ContainsKey("pvp_rankings_great_league"));
    }

    [Fact]
    public async Task BuildAsyncZeroPokemonIdDefaultsToPlaceholder()
    {
        var ctx = Ctx(new
        {
            pokemon_id = 0
        });
        var result = await this._sut.BuildAsync(ctx);
        var pokemonId = (int)result.Webhook["pokemon_id"];
        Assert.NotEqual(0, pokemonId);
    }

    [Fact]
    public async Task BuildAsyncDisappearTimeIsInFuture()
    {
        var now = DateTimeOffset.UtcNow;
        var ctx = new TestPayloadContext("pokemon", ToJson(new
        {
            pokemon_id = 184
        }), 0.0, 0.0, now);

        var result = await this._sut.BuildAsync(ctx);

        var disappear = (long)result.Webhook["disappear_time"];
        Assert.True(disappear > now.ToUnixTimeSeconds(), "disappear_time must be in the future");
    }

    private static TestPayloadContext Ctx(object alarm) =>
        new("pokemon", ToJson(alarm), 40.7128, -74.006, DateTimeOffset.UtcNow);

    private static readonly JsonSerializerOptions SnakeCaseOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private static JsonElement ToJson(object value)
    {
        var json = JsonSerializer.Serialize(value, SnakeCaseOptions);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
