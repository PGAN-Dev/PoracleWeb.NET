using System.Text.Json;
using System.Text.Json.Nodes;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

/// <param name="Item">What the scan reports.</param>
/// <param name="RepairedV1Row">The v1 row to PUT for a repairable kind; null for <see cref="RuleRepairKind.CannotEdit"/>.</param>
/// <param name="NeedsDryRunToConfirm">
/// True when the v2 read cannot say whether the rule needs the repair, so only a dry run of the PUT
/// (<c>updated</c> or <c>unchanged</c>) decides.
/// </param>
internal sealed record RuleRepairCandidate(RuleRepairItem Item, JsonElement? RepairedV1Row, bool NeedsDryRunToConfirm);

/// <summary>
/// Decides, for one stored v2 rule, whether it needs nothing, can be repaired, or can only be reported.
/// </summary>
internal static class RuleRepairClassifier
{
    private const int ByLevel = 9000;

    public static RuleRepairCandidate? Classify(StoredRule rule, string humanId)
    {
        // Pokestop events have their own route and no v1 translation; they are not part of this repair.
        if (string.Equals(rule.Type, "incident", StringComparison.Ordinal))
        {
            return null;
        }

        JsonElement row;
        try
        {
            row = TrackingV2Translator.ToV1Row(rule.Type, rule.Rule, humanId);
        }
        catch (Exception ex) when (ex is InvalidOperationException or JsonException or FormatException or InvalidCastException)
        {
            return CannotEdit(rule, humanId, ex.Message);
        }

        if (!TrackingV2Translator.TryTranslate(rule.Type, row, serverBounds: null, serverDeclared: null, out _, out var unsupported))
        {
            return CannotEdit(rule, humanId, unsupported ?? "v2 cannot express this rule");
        }

        switch (rule.Type)
        {
            case "invasion" when row.TryGetProperty("grunt_type", out var grunt)
                                 && grunt.ValueKind == JsonValueKind.String
                                 && string.Equals(grunt.GetString(), "metal", StringComparison.OrdinalIgnoreCase):
                return Repair(rule, humanId, RuleRepairKind.MetalGrunt, row, "grunt_type", JsonValue.Create("steel"), "grunt_type metal", "grunt_type steel", false);

            case "raid" when row.TryGetProperty("pokemon_id", out var boss)
                             && boss.ValueKind == JsonValueKind.Number
                             && boss.TryGetInt32(out var id)
                             && id == 0:
                return Repair(rule, humanId, RuleRepairKind.RaidPokemonZero, row, "pokemon_id", JsonValue.Create(ByLevel), "pokemon_id 0", "by level", false);

            case "raid" when IsAnyBossAnyLevel(rule.Rule):
                return Repair(rule, humanId, RuleRepairKind.RaidAnyBossLevel9000, row, null, null, "any boss, level unconfirmed", "any boss, any tier", true);

            case "maxbattle" when IsAnyBossAnyLevel(rule.Rule):
                return Repair(rule, humanId, RuleRepairKind.MaxBattleAnyBossLevel9000, row, null, null, "any boss, level unconfirmed", "any boss, any tier", true);

            default:
                return null;
        }
    }

    private static bool IsAnyBossAnyLevel(JsonElement v2) =>
        IsNullOrAbsent(v2, "pokemon_id") && IsNullOrAbsent(v2, "level");

    private static bool IsNullOrAbsent(JsonElement rule, string name) =>
        !rule.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null;

    private static RuleRepairCandidate Repair(
        StoredRule rule,
        string humanId,
        RuleRepairKind kind,
        JsonElement row,
        string? property,
        JsonNode? value,
        string before,
        string after,
        bool needsDryRun)
    {
        var repaired = row;
        if (property is not null)
        {
            var node = JsonNode.Parse(row.GetRawText())!.AsObject();
            node[property] = value;
            repaired = JsonSerializer.SerializeToElement(node);
        }

        return new RuleRepairCandidate(
            new RuleRepairItem(humanId, rule.Type, rule.ProfileNo, rule.Uid, kind, before, after, null),
            repaired,
            needsDryRun);
    }

    private static RuleRepairCandidate CannotEdit(StoredRule rule, string humanId, string reason) =>
        new(new RuleRepairItem(humanId, rule.Type, rule.ProfileNo, rule.Uid, RuleRepairKind.CannotEdit, "stored rule", null, reason), null, false);
}
