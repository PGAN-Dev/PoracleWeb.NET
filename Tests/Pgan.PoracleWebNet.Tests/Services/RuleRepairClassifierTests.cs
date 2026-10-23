using System.Text.Json;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

public class RuleRepairClassifierTests
{
    private static StoredRule Rule(string type, string json) =>
        new(type, 1, 42, JsonDocument.Parse(json).RootElement.Clone());

    private static RuleRepairCandidate? Classify(string type, string json) =>
        RuleRepairClassifier.Classify(Rule(type, json), "user1");

    [Fact]
    public void MetalBecomesSteel()
    {
        var result = Classify("invasion", """{"grunt_type":"metal","gender":"any"}""");

        Assert.NotNull(result);
        Assert.Equal(RuleRepairKind.MetalGrunt, result.Item.Kind);
        Assert.False(result.NeedsDryRunToConfirm);
        Assert.Equal("steel", result.RepairedV1Row!.Value.GetProperty("grunt_type").GetString());
        Assert.Equal(42, result.Item.Uid);
        Assert.Equal("user1", result.Item.HumanId);
    }

    [Fact]
    public void MetalMatchIsCaseInsensitive()
    {
        var result = Classify("invasion", """{"grunt_type":"Metal"}""");

        Assert.Equal(RuleRepairKind.MetalGrunt, result?.Item.Kind);
    }

    [Fact]
    public void SteelIsLeftAlone() => Assert.Null(Classify("invasion", """{"grunt_type":"steel"}"""));

    [Fact]
    public void RaidPokemonZeroBecomesByLevelKeepingItsLevel()
    {
        var result = Classify("raid", """{"pokemon_id":0,"level":5}""");

        Assert.NotNull(result);
        Assert.Equal(RuleRepairKind.RaidPokemonZero, result.Item.Kind);
        Assert.False(result.NeedsDryRunToConfirm);
        Assert.Equal(9000, result.RepairedV1Row!.Value.GetProperty("pokemon_id").GetInt32());
        Assert.Equal(5, result.RepairedV1Row!.Value.GetProperty("level").GetInt32());
    }

    [Fact]
    public void RaidNamedBossIsLeftAlone() => Assert.Null(Classify("raid", """{"pokemon_id":150,"level":5}"""));

    [Fact]
    public void RaidByLevel5IsLeftAlone() => Assert.Null(Classify("raid", """{"pokemon_id":null,"level":5}"""));

    [Fact]
    public void RaidAnyTierNeedsDryRun()
    {
        var result = Classify("raid", """{"pokemon_id":null,"level":null}""");

        Assert.NotNull(result);
        Assert.Equal(RuleRepairKind.RaidAnyBossLevel9000, result.Item.Kind);
        Assert.True(result.NeedsDryRunToConfirm);
        Assert.Equal(90, result.RepairedV1Row!.Value.GetProperty("level").GetInt32());
    }

    [Fact]
    public void MaxBattleAnyTierNeedsDryRun()
    {
        var result = Classify("maxbattle", """{"pokemon_id":null,"level":null}""");

        Assert.NotNull(result);
        Assert.Equal(RuleRepairKind.MaxBattleAnyBossLevel9000, result.Item.Kind);
        Assert.True(result.NeedsDryRunToConfirm);
    }

    [Fact]
    public void EggLevelZeroCannotBeEdited()
    {
        var result = Classify("egg", """{"level":0}""");

        Assert.NotNull(result);
        Assert.Equal(RuleRepairKind.CannotEdit, result.Item.Kind);
        Assert.Null(result.RepairedV1Row);
        Assert.Contains("level", result.Item.Reason);
    }

    [Fact]
    public void EggLevelFiveNeedsNothing() => Assert.Null(Classify("egg", """{"level":5}"""));

    [Fact]
    public void PvpLeagueOutsideTheFourCannotBeEdited()
    {
        var result = Classify("pokemon", """{"pokemon_id":25,"pvp_ranking_league":1000}""");

        Assert.NotNull(result);
        Assert.Equal(RuleRepairKind.CannotEdit, result.Item.Kind);
        Assert.False(string.IsNullOrEmpty(result.Item.Reason));
    }

    [Fact]
    public void PvpLeagueGreatNeedsNothing() =>
        Assert.Null(Classify("pokemon", """{"pokemon_id":25,"pvp_ranking_league":1500}"""));

    [Fact]
    public void OrdinaryPokemonRuleNeedsNothing() =>
        Assert.Null(Classify("pokemon", """{"pokemon_id":25,"min_iv":90}"""));

    [Fact]
    public void IncidentRuleNeedsNothing() =>
        Assert.Null(Classify("incident", """{"display_type":8}"""));

    private static JsonElement Body(string type, RuleRepairCandidate candidate)
    {
        Assert.True(
            TrackingV2Translator.TryTranslate(type, candidate.RepairedV1Row!.Value, null, null, out var body, out var why),
            why);
        return body;
    }

    [Fact]
    public void RepairedMetalRuleTranslatesToSteelKeepingEveryOtherField()
    {
        var result = Classify(
            "invasion",
            """{"grunt_type":"metal","gender":"female","distance":750,"template":"fancy","ping":"<@123>","override_areas":["north"],"clean":true}""");

        var body = Body("invasion", result!);

        Assert.Equal("steel", body.GetProperty("grunt_type").GetString());
        Assert.Equal("female", body.GetProperty("gender").GetString());
        Assert.Equal(750, body.GetProperty("distance").GetInt32());
        Assert.Equal("fancy", body.GetProperty("template").GetString());
        Assert.Equal("<@123>", body.GetProperty("ping").GetString());
        Assert.Equal("north", body.GetProperty("override_areas")[0].GetString());
        Assert.True(body.GetProperty("clean").GetBoolean());
    }

    [Fact]
    public void RepairedRaidPokemonZeroTranslatesToByLevelKeepingItsFields()
    {
        var result = Classify("raid", """{"pokemon_id":0,"level":5,"distance":900,"template":"fancy","clean":true}""");

        var body = Body("raid", result!);

        Assert.Equal(5, body.GetProperty("level").GetInt32());
        Assert.False(body.TryGetProperty("pokemon_id", out _));
        Assert.Equal(900, body.GetProperty("distance").GetInt32());
        Assert.Equal("fancy", body.GetProperty("template").GetString());
        Assert.True(body.GetProperty("clean").GetBoolean());
    }

    [Fact]
    public void RepairedRaidPokemonZeroWithNoLevelIsAnyBossAnyTier()
    {
        var result = Classify("raid", """{"pokemon_id":0,"level":null,"distance":900}""");

        Assert.Equal(RuleRepairKind.RaidPokemonZero, result?.Item.Kind);
        var body = Body("raid", result!);

        Assert.False(body.TryGetProperty("pokemon_id", out _));
        Assert.False(body.TryGetProperty("level", out _));
        Assert.Equal(900, body.GetProperty("distance").GetInt32());
    }

    [Fact]
    public void RepairedAnyTierRaidTranslatesWithoutLevelOrBossKeepingItsFields()
    {
        var result = Classify("raid", """{"pokemon_id":null,"level":null,"distance":900,"ping":"<@123>","clean":true}""");

        var body = Body("raid", result!);

        Assert.False(body.TryGetProperty("pokemon_id", out _));
        Assert.False(body.TryGetProperty("level", out _));
        Assert.Equal(900, body.GetProperty("distance").GetInt32());
        Assert.Equal("<@123>", body.GetProperty("ping").GetString());
        Assert.True(body.GetProperty("clean").GetBoolean());
    }

    [Fact]
    public void RepairedAnyTierMaxBattleTranslatesWithoutLevelOrBossKeepingItsFields()
    {
        var result = Classify("maxbattle", """{"pokemon_id":null,"level":null,"distance":900,"template":"fancy"}""");

        var body = Body("maxbattle", result!);

        Assert.False(body.TryGetProperty("pokemon_id", out _));
        Assert.False(body.TryGetProperty("level", out _));
        Assert.Equal(900, body.GetProperty("distance").GetInt32());
        Assert.Equal("fancy", body.GetProperty("template").GetString());
    }

    [Fact]
    public void AnyTierRepairedRowIsTheStoredRuleReadAsV1()
    {
        var stored = Rule("raid", """{"pokemon_id":null,"level":null,"distance":900}""");

        var result = RuleRepairClassifier.Classify(stored, "user1");

        Assert.Equal(9000, result!.RepairedV1Row!.Value.GetProperty("pokemon_id").GetInt32());
        Assert.Equal(
            TrackingV2Translator.ToV1Row("raid", stored.Rule, "user1").GetRawText(),
            result.RepairedV1Row!.Value.GetRawText());
        Assert.Equal("any boss, level unconfirmed", result.Item.Before);
    }

    [Fact]
    public void UnreadableStoredValueIsReportedNotThrown()
    {
        var result = Classify("invasion", """{"grunt_type":"metal","gender":"robot"}""");

        Assert.Equal(RuleRepairKind.CannotEdit, result?.Item.Kind);
        Assert.Null(result!.RepairedV1Row);
    }

    // A field the server's own schema declares is one an edit sends back, so the rule is editable and the
    // scan has nothing to say about it. Every personal shiny rule was listed as uneditable without this.
    [Fact]
    public void FieldTheServerDeclaresIsNotReportedAsUneditable()
    {
        var stored = Rule("pokemon", """{"pokemon_id":25,"personal_shiny":true}""");

        Assert.Null(RuleRepairClassifier.Classify(stored, "user1", new HashSet<string> { "personal_shiny" }));
    }

    [Fact]
    public void FieldTheServerDoesNotDeclareIsStillReported()
    {
        var stored = Rule("pokemon", """{"pokemon_id":25,"personal_shiny":true}""");

        Assert.Equal(RuleRepairKind.CannotEdit, RuleRepairClassifier.Classify(stored, "user1", new HashSet<string> { "ping" })?.Item.Kind);
        Assert.Equal(RuleRepairKind.CannotEdit, Classify("pokemon", """{"pokemon_id":25,"personal_shiny":true}""")?.Item.Kind);
    }

    [Fact]
    public void DeclaredFieldOnARepairableRuleStillRepairs()
    {
        var stored = Rule("invasion", """{"grunt_type":"metal","future_flag":true}""");

        var result = RuleRepairClassifier.Classify(stored, "user1", new HashSet<string> { "future_flag" });

        Assert.Equal(RuleRepairKind.MetalGrunt, result?.Item.Kind);
        Assert.True(result!.RepairedV1Row!.Value.GetProperty("future_flag").GetBoolean());
    }
}
