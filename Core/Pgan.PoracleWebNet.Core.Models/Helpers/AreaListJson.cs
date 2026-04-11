using System.Text.Json;

namespace Pgan.PoracleWebNet.Core.Models.Helpers;

/// <summary>
/// Shared helpers for parsing the <c>humans.area</c> and <c>profiles.area</c> JSON columns.
/// Both are stored as a JSON-encoded string array (e.g. <c>["downtown","west end"]</c>),
/// with a legacy fallback to comma-separated values for rows written by older PoracleWeb
/// versions. Kept in <c>Core.Models</c> so both the services layer and the repositories layer
/// can reach it without introducing a new cross-assembly reference.
/// </summary>
public static class AreaListJson
{
    /// <summary>
    /// Parses an area column value into a mutable list. Returns an empty list for null/empty
    /// input. Accepts both JSON array format (preferred) and legacy comma-separated format.
    /// Input that starts with <c>[</c> or <c>{</c> is treated as JSON only — a failed JSON parse
    /// on bracketed input returns an empty list rather than falling through to the CSV split,
    /// which would otherwise turn garbage like <c>[1,2,3]</c> into <c>["[1", "2", "3]"]</c>.
    /// </summary>
    public static List<string> Parse(string? areaJson)
    {
        if (string.IsNullOrWhiteSpace(areaJson))
        {
            return [];
        }

        var trimmed = areaJson.AsSpan().TrimStart();
        var looksLikeJson = trimmed.Length > 0 && (trimmed[0] == '[' || trimmed[0] == '{');

        try
        {
            return JsonSerializer.Deserialize<List<string>>(areaJson) ?? [];
        }
        catch (JsonException)
        {
            if (looksLikeJson)
            {
                // Malformed or wrong-shape JSON — never fall through to CSV, which would
                // produce bracketed garbage from the failed JSON string.
                return [];
            }

            // Legacy fallback: comma-separated values (pre-JSON PoracleWeb rows).
            return [.. areaJson.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
        }
    }

    /// <summary>
    /// Serializes an area list back to the JSON column format. Always returns <c>"[]"</c>
    /// for empty input so the column never holds an empty string (which would parse back
    /// to an empty list but violates the NOT NULL + expected-shape contract).
    /// </summary>
    public static string Serialize(IReadOnlyList<string> areas) =>
        areas.Count == 0 ? "[]" : JsonSerializer.Serialize(areas);
}
