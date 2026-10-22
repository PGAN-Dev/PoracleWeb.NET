using System.Text.Json;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

/// <summary>
/// The three mutual-exclusion rules PoracleNG applies to a per-alarm scope, mirrored so the refusal arrives
/// before any write and with wording a person can act on.
/// </summary>
/// <remarks>
/// A place and a set of areas are two different answers to the same question, so a row cannot carry both.
/// A place is an anchor for a radius, so it needs one. Areas replace the radius entirely, so they cannot
/// coexist with one. PoracleNG enforces the same rules (<c>validateOverrideFields</c>); checking here also
/// covers the callers that never touch a controller, which is where profile import and quick-pick apply
/// slipped past their guards before (#548, #565).
/// </remarks>
internal static class OverrideScope
{
    /// <summary>Throws <see cref="AlarmValidationException"/> for the first row whose scope is incoherent.</summary>
    /// <param name="body">One v1-shaped row, or an array of them.</param>
    public static void EnsureCoherent(JsonElement body)
    {
        foreach (var row in RowsOf(body))
        {
            var label = row.TryGetProperty("override_location_label", out var l)
                && l.ValueKind == JsonValueKind.String
                    ? l.GetString()
                    : null;
            var hasLabel = !string.IsNullOrWhiteSpace(label);
            var hasAreas = AreasOf(row).Count > 0;
            var distance = row.TryGetProperty("distance", out var d) && d.ValueKind == JsonValueKind.Number
                ? d.GetInt32()
                : 0;

            if (hasLabel && hasAreas)
            {
                throw new AlarmValidationException(
                    "An alarm can be limited to a place or to areas, not both.");
            }

            if (hasAreas && distance > 0)
            {
                throw new AlarmValidationException(
                    "An alarm limited to areas cannot also have a radius. Clear one of them.");
            }

            if (hasLabel && distance == 0)
            {
                throw new AlarmValidationException(
                    "An alarm measured from a place needs a radius.");
            }
        }
    }

    /// <summary>The row's <c>override_areas</c> names; empty when it has none, or null.</summary>
    public static IReadOnlyList<string> AreasOf(JsonElement row) =>
        row.ValueKind == JsonValueKind.Object
        && row.TryGetProperty("override_areas", out var areas)
        && areas.ValueKind == JsonValueKind.Array
            ? areas.EnumerateArray()
                .Where(a => a.ValueKind == JsonValueKind.String)
                .Select(a => a.GetString()!)
                .ToList()
            : [];

    private static IEnumerable<JsonElement> RowsOf(JsonElement body) =>
        body.ValueKind switch
        {
            JsonValueKind.Array => body.EnumerateArray().Where(r => r.ValueKind == JsonValueKind.Object),
            JsonValueKind.Object => [body],
            _ => [],
        };
}
