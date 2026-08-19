using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Mappings;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// Every column PoracleNG stores for a pokemon or quest rule is either written by PoracleWeb or listed
/// here with a reason.
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
/// </remarks>
public class TrackingFieldCoverageTests
{
    /// <summary>
    /// <c>MonsterTrackingAPI</c> in <c>processor/internal/db/tracking_queries.go</c> at 5.1.0
    /// (<c>c5e08cb4</c>), which is what production runs.
    /// </summary>
    private static readonly string[] PoracleNgFields =
    [
        "uid", "id", "profile_no", "ping", "clean", "distance", "template", "pokemon_id", "form",
        "min_iv", "max_iv", "min_cp", "max_cp", "min_level", "max_level",
        "atk", "def", "sta", "max_atk", "max_def", "max_sta",
        "gender", "min_weight", "max_weight", "min_time", "rarity", "max_rarity", "size", "max_size",
        "pvp_ranking_league", "pvp_ranking_best", "pvp_ranking_worst",
        "pvp_ranking_min_cp", "pvp_ranking_cap", "pvp_ranking_evolution",
        "override_location_label", "override_areas",
    ];

    /// <summary>Fields PoracleWeb leaves to PoracleNG, and why.</summary>
    private static readonly Dictionary<string, string> NotSent = new(StringComparer.Ordinal)
    {
        ["uid"] = "Only carried on an edit. PoracleNG treats uid: 0 as an update of a row with that uid, "
            + "so an insert drops it and lets PoracleNG assign one.",
        ["profile_no"] = "Stamped from a JWT claim that goes stale; omitting it files the alarm under the live profile. #411",
        ["rarity"] = "PoracleNG reads rarity per species from rolling sighting stats, and PoracleWeb only writes "
            + "species-specific rules, so the filter is a constant on every rule it can create: no-op or permanent "
            + "mute. Zero of 17,420 production rules set it.",
        ["max_rarity"] = "See rarity.",
    };

    private readonly Mock<IPoracleTrackingProxy> _proxy = new();
    private readonly Mock<IFeatureGate> _featureGate = new();
    private JsonElement _sent;

    public TrackingFieldCoverageTests()
    {
        this._featureGate.Setup(g => g.EnsureEnabledAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        this._proxy
            .Setup(p => p.CreateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<JsonElement>()))
            .Callback<string, string, JsonElement>((_, _, body) => this._sent = body.Clone())
            .ReturnsAsync(new TrackingCreateResult([1], 0, 0, 1));
    }

    private async Task<JsonElement> WrittenRowAsync()
    {
        await new MonsterService(this._proxy.Object, this._featureGate.Object)
            .CreateAsync("u1", new MonsterCreate { PokemonId = 201 }.ToMonster());

        return this._sent.ValueKind == JsonValueKind.Array ? this._sent.EnumerateArray().First() : this._sent;
    }

    [Fact]
    public async Task EveryPoracleNgFieldIsEitherWrittenOrExcusedInWriting()
    {
        var row = await this.WrittenRowAsync();
        var written = row.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

        var unaccounted = PoracleNgFields
            .Where(f => !written.Contains(f) && !NotSent.ContainsKey(f))
            .ToList();

        Assert.True(
            unaccounted.Count == 0,
            "PoracleNG stores these and PoracleWeb neither writes them nor explains why: "
            + string.Join(", ", unaccounted)
            + ". Add the property to Monster/MonsterCreate/MonsterUpdate and the mapping, or add it to "
            + "NotSent with the reason.");
    }

    [Fact]
    public async Task TheExcusedFieldsAreReallyAbsent()
    {
        // The other half: an excuse that no longer matches the code is worse than no excuse, because it
        // reads as a decision. uid is dropped only when zero, so it is not a candidate here.
        var written = (await this.WrittenRowAsync()).EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

        var contradicted = NotSent.Keys.Where(written.Contains).ToList();

        Assert.True(contradicted.Count == 0, "Listed as not sent, but sent: " + string.Join(", ", contradicted));
    }

    /// <summary><c>QuestTrackingAPI</c> at the same commit.</summary>
    private static readonly string[] PoracleNgQuestFields =
    [
        "uid", "id", "profile_no", "ping", "clean", "reward", "template", "shiny", "reward_type",
        "distance", "form", "amount", "override_location_label", "override_areas",
    ];

    [Fact]
    public async Task EveryPoracleNgQuestFieldIsEitherWrittenOrExcusedInWriting()
    {
        var remapper = new Mock<ITrackedUidRemapper>();
        remapper
            .Setup(r => r.RemapAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns(Task.CompletedTask);

        await new QuestService(
            this._proxy.Object, this._featureGate.Object, NullLogger<QuestService>.Instance, remapper.Object)
            .CreateAsync("u1", new QuestCreate { Reward = 1, RewardType = 7 }.ToQuest());

        var row = this._sent.ValueKind == JsonValueKind.Array ? this._sent.EnumerateArray().First() : this._sent;
        var written = row.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

        var unaccounted = PoracleNgQuestFields
            .Where(f => !written.Contains(f) && !NotSent.ContainsKey(f))
            .ToList();

        Assert.True(unaccounted.Count == 0, "Not written and not explained: " + string.Join(", ", unaccounted));
    }

    [Fact]
    public void TheExcuseListOnlyNamesFieldsPoracleNgHas()
    {
        var unknown = NotSent.Keys.Except(PoracleNgFields, StringComparer.Ordinal).ToList();

        Assert.True(unknown.Count == 0, "Excused a field PoracleNG does not have: " + string.Join(", ", unknown));
    }
}
