using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Mappings;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// The mega-evolution and time-remaining filters, from the JSON the browser sends to the row PoracleNG
/// stores and back.
/// </summary>
/// <remarks>
/// Deliberately bound from JSON rather than constructed, because that is where the evolution selector
/// was lost: the property existed in the Angular request and in no C# model, so model binding dropped it
/// silently and the specs that "covered" it only ever checked what the component put in the request.
/// Validation attributes live on the <c>*Create</c> DTO, so a test that builds a <see cref="Monster"/>
/// validates nothing (#548, #555, #565).
/// </remarks>
public class PokemonFilterRoundTripTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private readonly Mock<IPoracleTrackingProxy> _proxy = new();
    private readonly Mock<IFeatureGate> _featureGate = new();
    private JsonElement _sent;

    public PokemonFilterRoundTripTests()
    {
        this._featureGate.Setup(g => g.EnsureEnabledAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        this._proxy
            .Setup(p => p.CreateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<JsonElement>()))
            .Callback<string, string, JsonElement>((_, _, body) => this._sent = body.Clone())
            .ReturnsAsync(new TrackingCreateResult([1], 0, 0, 1));
    }

    /// <summary>The body the PVP tab posts for "Great league, rank 1-100, Mega X, 5 minutes left".</summary>
    private const string BrowserBody =
        """
        {
          "pokemonId": 6,
          "distance": 0,
          "pvpRankingLeague": 1500,
          "pvpRankingBest": 1,
          "pvpRankingWorst": 100,
          "pvpRankingEvolution": 2,
          "minTime": 300
        }
        """;

    private async Task<JsonElement> WriteAsync(MonsterCreate create)
    {
        await new MonsterService(this._proxy.Object, this._featureGate.Object).CreateAsync("u1", create.ToMonster());

        return this._sent.ValueKind == JsonValueKind.Array ? this._sent.EnumerateArray().First() : this._sent;
    }

    [Fact]
    public async Task TheBrowsersEvolutionChoiceReachesPoracleNg()
    {
        var create = JsonSerializer.Deserialize<MonsterCreate>(BrowserBody, Web)!;

        Assert.Equal(2, create.PvpRankingEvolution);
        Assert.Equal(2, (await this.WriteAsync(create)).GetProperty("pvp_ranking_evolution").GetInt32());
    }

    [Fact]
    public async Task TheBrowsersTimeRemainingReachesPoracleNg()
    {
        var create = JsonSerializer.Deserialize<MonsterCreate>(BrowserBody, Web)!;

        Assert.Equal(300, create.MinTime);
        Assert.Equal(300, (await this.WriteAsync(create)).GetProperty("min_time").GetInt32());
    }

    [Fact]
    public async Task ARuleWithNeitherFilterStillWritesTheDefaults()
    {
        // The legitimate-case half. Both fields default to "no filter", and PoracleNG stores what it is
        // sent, so a plain rule must not arrive carrying someone's leftover mega or a 5-minute floor.
        var row = await this.WriteAsync(new MonsterCreate { PokemonId = 201 });

        Assert.Equal(0, row.GetProperty("pvp_ranking_evolution").GetInt32());
        Assert.Equal(0, row.GetProperty("min_time").GetInt32());
    }

    [Fact]
    public async Task AStoredRuleReadsBackWithBothFilters()
    {
        // Without this the card cannot say what the rule does, which is how the selector looked like it
        // worked: the value was dropped on the way out as well as on the way in.
        this._proxy
            .Setup(p => p.GetByUserAsync("pokemon", "u1"))
            .ReturnsAsync(JsonDocument.Parse(
                """[{"uid":7,"pokemon_id":6,"pvp_ranking_evolution":3,"min_time":600}]""").RootElement.Clone());

        var monster = await new MonsterService(this._proxy.Object, this._featureGate.Object).GetByUidAsync("u1", 7);

        Assert.Equal(3, monster!.PvpRankingEvolution);
        Assert.Equal(600, monster.MinTime);
    }

    [Fact]
    public void AnEditThatSaysNothingAboutThemLeavesThemAlone()
    {
        var existing = new Monster { Uid = 7, PokemonId = 6, PvpRankingEvolution = 2, MinTime = 300 };

        new MonsterUpdate { Distance = 1000 }.ApplyUpdate(existing);

        Assert.Equal(2, existing.PvpRankingEvolution);
        Assert.Equal(300, existing.MinTime);
    }

    [Fact]
    public void AnEditThatChangesThemChangesThem()
    {
        var existing = new Monster { Uid = 7, PokemonId = 6, PvpRankingEvolution = 2, MinTime = 300 };

        new MonsterUpdate { PvpRankingEvolution = 0, MinTime = 0 }.ApplyUpdate(existing);

        Assert.Equal(0, existing.PvpRankingEvolution);
        Assert.Equal(0, existing.MinTime);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    [InlineData(-1, false)]
    public void EvolutionAcceptsOnlyTheFourFormsPoracleNgRanks(int value, bool accepted) =>
        Assert.Equal(accepted, Validate(new MonsterCreate(), nameof(MonsterCreate.PvpRankingEvolution), value));

    [Theory]
    [InlineData(0, true)]
    [InlineData(300, true)]
    [InlineData(3600, true)]
    [InlineData(3601, false)]
    [InlineData(-1, false)]
    public void TimeRemainingIsCappedAtASpawnsLongestPossibleLife(int value, bool accepted) =>
        Assert.Equal(accepted, Validate(new MonsterCreate(), nameof(MonsterCreate.MinTime), value));

    private static bool Validate(object instance, string member, object? value) =>
        Validator.TryValidateProperty(value, new ValidationContext(instance) { MemberName = member }, []);
}
