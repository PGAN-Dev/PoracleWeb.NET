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
}
