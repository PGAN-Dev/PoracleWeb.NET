using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Mappings;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// Every column PoracleNG stores, for every alarm type, is either written by PoracleWeb or listed here
/// with a reason.
/// </summary>
/// <remarks>
/// <para>
/// <c>pvp_ranking_evolution</c> shipped as a control in the PVP tab, a field in the Angular request and
/// four passing component specs, while no C# model carried the property — so model binding dropped it on
/// the way in, the typed deserialize dropped it on the way back, and the selector changed nothing. The
/// component specs passed because they assert the shape of the request the component builds, which says
/// nothing about whether the API accepts it.
/// </para>
/// <para>
/// A field PoracleWeb does not send is not automatically a bug: PoracleNG fills its own default, and
/// <see cref="TrackingFieldPreserver"/> carries a stored value forward, so a value set with the bot
/// survives a web edit either way. What is a bug is not noticing. When PoracleNG grows a column, this
/// test fails and the choice gets made deliberately rather than by omission.
/// </para>
/// <para>
/// The column lists are the <c>*TrackingAPI</c> structs in
/// <c>processor/internal/db/tracking_queries.go</c> at 5.1.0 (<c>c5e08cb4</c>), which is the commit
/// production runs. Ten types, because a guarantee that covers two of them is the same guarantee that
/// let the mega picker ship broken.
/// </para>
/// </remarks>
public class TrackingFieldCoverageTests
{
    private static readonly Dictionary<string, string[]> PoracleNgColumns = new(StringComparer.Ordinal)
    {
        ["pokemon"] =
        [
            "uid", "id", "profile_no", "ping", "clean", "distance", "template", "pokemon_id", "form",
            "min_iv", "max_iv", "min_cp", "max_cp", "min_level", "max_level",
            "atk", "def", "sta", "max_atk", "max_def", "max_sta",
            "gender", "min_weight", "max_weight", "min_time", "rarity", "max_rarity", "size", "max_size",
            "pvp_ranking_league", "pvp_ranking_best", "pvp_ranking_worst",
            "pvp_ranking_min_cp", "pvp_ranking_cap", "pvp_ranking_evolution",
            "override_location_label", "override_areas",
        ],
        ["raid"] =
        [
            "uid", "id", "profile_no", "ping", "clean", "distance", "template", "team", "pokemon_id",
            "form", "level", "exclusive", "move", "evolution", "gym_id", "rsvp_changes",
            "override_location_label", "override_areas",
        ],
        ["egg"] =
        [
            "uid", "id", "profile_no", "ping", "clean", "distance", "template", "team", "level",
            "exclusive", "gym_id", "rsvp_changes", "override_location_label", "override_areas",
        ],
        ["quest"] =
        [
            "uid", "id", "profile_no", "ping", "clean", "distance", "template", "reward_type", "reward",
            "form", "shiny", "amount", "override_location_label", "override_areas",
        ],
        ["invasion"] =
        [
            "uid", "id", "profile_no", "ping", "clean", "distance", "template", "gender", "grunt_type",
            "override_location_label", "override_areas",
        ],
        ["lure"] =
        [
            "uid", "id", "profile_no", "ping", "clean", "distance", "template", "lure_id",
            "override_location_label", "override_areas",
        ],
        ["nest"] =
        [
            "uid", "id", "profile_no", "ping", "clean", "distance", "template", "pokemon_id",
            "min_spawn_avg", "form", "override_location_label", "override_areas",
        ],
        ["gym"] =
        [
            "uid", "id", "profile_no", "ping", "clean", "distance", "template", "team", "slot_changes",
            "battle_changes", "gym_id", "override_location_label", "override_areas",
        ],
        ["fort"] =
        [
            "uid", "id", "profile_no", "ping", "distance", "template", "fort_type", "include_empty",
            "change_types", "override_location_label", "override_areas",
        ],
        ["maxbattle"] =
        [
            "uid", "id", "profile_no", "ping", "clean", "distance", "template", "pokemon_id", "form",
            "level", "move", "gmax", "evolution", "station_id", "override_location_label", "override_areas",
        ],
    };

    /// <summary>Columns PoracleWeb leaves to PoracleNG, keyed by <c>type.column</c>, and why.</summary>
    private static readonly Dictionary<string, string> NotSent = new(StringComparer.Ordinal)
    {
        ["*.uid"] = "Only carried on an edit. PoracleNG treats uid: 0 as an update of a row with that uid, "
            + "so an insert drops it and lets PoracleNG assign one.",
        ["*.profile_no"] = "Stamped from a JWT claim that goes stale; omitting it files the alarm under the live profile. #411",
        ["pokemon.rarity"] = "Rarity is a per-species tier PoracleNG recomputes from rolling sighting stats, so on the "
            + "species-specific rules PoracleWeb creates the filter is a constant: no-op or permanent mute. It would "
            + "mean something on a track-everything rule, which only the bot can create — and zero of production's "
            + "17,420 pokemon rules set it. Values set with the bot survive a web edit either way.",
        ["pokemon.max_rarity"] = "See pokemon.rarity.",
    };

    private readonly Mock<IPoracleTrackingProxy> _proxy = new();
    private readonly Mock<IFeatureGate> _featureGate = new();
    private readonly Mock<ITrackedUidRemapper> _remapper = new();
    private JsonElement _sent;

    public TrackingFieldCoverageTests()
    {
        this._featureGate.Setup(g => g.EnsureEnabledAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        this._remapper
            .Setup(r => r.RemapAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns(Task.CompletedTask);
        // Several services read the user's existing rules before writing, to refuse a create that
        // PoracleNG would resolve into an update of a different alarm (#561). No rules, no collision.
        this._proxy
            .Setup(p => p.GetByUserAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(() => JsonDocument.Parse("[]").RootElement.Clone());
        this._proxy
            .Setup(p => p.CreateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<JsonElement>()))
            .Callback<string, string, JsonElement>((_, _, body) => this._sent = body.Clone())
            .ReturnsAsync(new TrackingCreateResult([1], 0, 0, 1));
    }

    public static TheoryData<string> TrackingTypes() =>
    [
        "pokemon", "raid", "egg", "quest", "invasion", "lure", "nest", "gym", "fort", "maxbattle",
    ];

    /// <summary>Creates one alarm of the given type and returns the row that reached the proxy.</summary>
    private async Task<HashSet<string>> WrittenColumnsAsync(string type)
    {
        await this.CreateAsync(type);

        var row = this._sent.ValueKind == JsonValueKind.Array ? this._sent.EnumerateArray().First() : this._sent;
        return row.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
    }

    private Task CreateAsync(string type) => type switch
    {
        "pokemon" => new MonsterService(this._proxy.Object, this._featureGate.Object)
            .CreateAsync("u1", new MonsterCreate { PokemonId = 201 }.ToMonster()),
        "raid" => new RaidService(
                this._proxy.Object, this._featureGate.Object, NullLogger<RaidService>.Instance, this._remapper.Object)
            .CreateAsync("u1", new RaidCreate { Level = 5, PokemonId = 9000 }.ToRaid()),
        "egg" => new EggService(
                this._proxy.Object, this._featureGate.Object, NullLogger<EggService>.Instance, this._remapper.Object)
            .CreateAsync("u1", new EggCreate { Level = 5 }.ToEgg()),
        "quest" => new QuestService(
                this._proxy.Object, this._featureGate.Object, NullLogger<QuestService>.Instance, this._remapper.Object)
            .CreateAsync("u1", new QuestCreate { Reward = 25, RewardType = 7 }.ToQuest()),
        "invasion" => new InvasionService(
                this._proxy.Object, this._featureGate.Object, NullLogger<InvasionService>.Instance, this._remapper.Object)
            .CreateAsync("u1", new InvasionCreate { GruntType = "blanche" }.ToInvasion()),
        "lure" => new LureService(
                this._proxy.Object, this._featureGate.Object, NullLogger<LureService>.Instance, this._remapper.Object)
            .CreateAsync("u1", new LureCreate { LureId = 501 }.ToLure()),
        "nest" => new NestService(
                this._proxy.Object, this._featureGate.Object, NullLogger<NestService>.Instance, this._remapper.Object)
            .CreateAsync("u1", new NestCreate { PokemonId = 201 }.ToNest()),
        "gym" => new GymService(
                this._proxy.Object, this._featureGate.Object, NullLogger<GymService>.Instance, this._remapper.Object)
            .CreateAsync("u1", new GymCreate { Team = 0 }.ToGym()),
        "fort" => new FortChangeService(
                this._proxy.Object, this._featureGate.Object, NullLogger<FortChangeService>.Instance, this._remapper.Object)
            .CreateAsync("u1", new FortChangeCreate { FortType = "everything" }.ToFortChange()),
        "maxbattle" => new MaxBattleService(
                this._proxy.Object, this._featureGate.Object, NullLogger<MaxBattleService>.Instance, this._remapper.Object)
            .CreateAsync("u1", new MaxBattleCreate { Level = 5 }.ToMaxBattle()),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "No fixture for this tracking type."),
    };

    private static bool IsExcused(string type, string column) =>
        NotSent.ContainsKey($"{type}.{column}") || NotSent.ContainsKey($"*.{column}");

    [Theory]
    [MemberData(nameof(TrackingTypes))]
    public async Task EveryColumnIsEitherWrittenOrExcusedInWriting(string type)
    {
        var written = await this.WrittenColumnsAsync(type);

        var unaccounted = PoracleNgColumns[type]
            .Where(c => !written.Contains(c) && !IsExcused(type, c))
            .ToList();

        Assert.True(
            unaccounted.Count == 0,
            $"PoracleNG stores these {type} columns and PoracleWeb neither writes them nor explains why: "
            + string.Join(", ", unaccounted)
            + ". Add the property to the model, the Create and Update DTOs and the mapping, or add it to "
            + "NotSent with the reason.");
    }

    [Theory]
    [MemberData(nameof(TrackingTypes))]
    public async Task TheExcusedColumnsAreReallyAbsent(string type)
    {
        // The other half: an excuse that no longer matches the code is worse than no excuse, because it
        // reads as a decision someone made. uid is dropped only when zero, which the fixtures are.
        var written = await this.WrittenColumnsAsync(type);

        var contradicted = PoracleNgColumns[type].Where(c => IsExcused(type, c) && written.Contains(c)).ToList();

        Assert.True(contradicted.Count == 0, $"Listed as not sent for {type}, but sent: " + string.Join(", ", contradicted));
    }

    [Fact]
    public void TheExcuseListOnlyNamesColumnsPoracleNgHas()
    {
        var unknown = NotSent.Keys
            .Where(key =>
            {
                var (type, column) = (key[..key.IndexOf('.', StringComparison.Ordinal)], key[(key.IndexOf('.', StringComparison.Ordinal) + 1)..]);
                return type == "*"
                    ? !PoracleNgColumns.Values.Any(columns => columns.Contains(column, StringComparer.Ordinal))
                    : !PoracleNgColumns.TryGetValue(type, out var columns) || !columns.Contains(column, StringComparer.Ordinal);
            })
            .ToList();

        Assert.True(unknown.Count == 0, "Excused a column PoracleNG does not have: " + string.Join(", ", unknown));
    }
}
