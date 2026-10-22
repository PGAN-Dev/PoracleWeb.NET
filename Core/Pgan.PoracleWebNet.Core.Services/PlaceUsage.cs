using System.Text.Json;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

/// <summary>
/// What a v1-shaped tracking snapshot says about where its alarms measure from: the one definition of
/// "aimed at a saved place", shared by bulk distance, the pin-clear warning and the place-delete check.
/// </summary>
public static class PlaceUsage
{
    /// <summary>True when the row names a saved place (<c>override_location_label</c> is not blank).</summary>
    public static bool HasPlace(JsonElement row) =>
        row.ValueKind == JsonValueKind.Object
        && row.TryGetProperty("override_location_label", out var label)
        && label.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(label.GetString());

    /// <summary>
    /// How many rules measure their radius from the profile pin: a radius above zero and no saved place.
    /// Clearing the pin stops every one of them matching.
    /// </summary>
    public static int CountMeasuredFromPin(JsonElement snapshot) =>
        Rows(snapshot).Count(r =>
            r.Row.TryGetProperty("distance", out var d)
            && d.ValueKind == JsonValueKind.Number
            && d.TryGetDouble(out var metres)
            && metres > 0
            && !HasPlace(r.Row));

    /// <summary>
    /// The rules aimed at the place called <paramref name="label"/>, compared exactly as PoracleNG stores it.
    /// </summary>
    public static IReadOnlyList<PlaceUsageRule> RulesAimedAt(JsonElement snapshot, string label) =>
        [.. Rows(snapshot)
            .Where(r => HasPlace(r.Row)
                && string.Equals(r.Row.GetProperty("override_location_label").GetString(), label, StringComparison.Ordinal))
            .Select(r => new PlaceUsageRule(TypeOf(r.Type, r.Row), UidOf(r.Row), DescriptionOf(r.Row)))];

    private static string? DescriptionOf(JsonElement row) =>
        row.TryGetProperty("description", out var desc)
        && desc.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(desc.GetString())
            ? desc.GetString()
            : null;

    /// <summary>The snapshot folds Pokéstop events into <c>invasion</c>; name them as what they are.</summary>
    private static string TypeOf(string type, JsonElement row) =>
        type == "invasion"
        && row.TryGetProperty("grunt_type", out var grunt)
        && grunt.ValueKind == JsonValueKind.String
        && PokestopEventTypes.IsEventName(grunt.GetString())
            ? "incident"
            : type;

    private static int UidOf(JsonElement row) =>
        row.TryGetProperty("uid", out var uid) && uid.ValueKind == JsonValueKind.Number && uid.TryGetInt32(out var n) ? n : 0;

    private static IEnumerable<(string Type, JsonElement Row)> Rows(JsonElement snapshot)
    {
        if (snapshot.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        foreach (var type in snapshot.EnumerateObject())
        {
            if (type.Value.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var row in type.Value.EnumerateArray())
            {
                if (row.ValueKind == JsonValueKind.Object)
                {
                    yield return (type.Name, row);
                }
            }
        }
    }
}
