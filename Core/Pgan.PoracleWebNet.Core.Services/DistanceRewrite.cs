using System.Text.Json;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

/// <summary>
/// The body a bulk "Update Distance" sends, built from the stored rows, leaving out the rows a radius
/// cannot apply to.
/// </summary>
/// <remarks>
/// <para>
/// PoracleNG refuses two scope combinations, and <see cref="UserOwnedOverrideAreaProxy"/> refuses them
/// again before the write: an alarm limited to areas that also carries a radius, and one measured from a
/// saved place that carries none. Rewriting every selected row therefore failed the whole selection the
/// moment it held one area-scoped alarm -- 400 "An alarm limited to areas cannot also have a radius",
/// nothing changed, and on max battles, which delete before they re-create, the refusal landed after the
/// delete.
/// </para>
/// <para>
/// Those rows are skipped rather than cleared of their scope. "Set these to 2 km" is not a request to
/// widen an alarm the user confined to one neighbourhood, and quietly doing so would be worse than
/// leaving it alone and saying so.
/// </para>
/// </remarks>
internal static class DistanceRewrite
{
    public static (JsonElement Body, DistanceUpdateResult Skipped) Build(
        JsonElement rows, Func<JsonElement, bool> include, int distance)
    {
        var areaScoped = new List<int>();
        var placeScoped = new List<int>();

        bool Writable(JsonElement row)
        {
            if (!include(row))
            {
                return false;
            }

            switch (ConflictOf(HasAreas(row), HasPlace(row), distance))
            {
                case ScopeConflict.AreaScoped:
                    Record(areaScoped, row);
                    return false;
                case ScopeConflict.PlaceScoped:
                    Record(placeScoped, row);
                    return false;
                default:
                    return true;
            }
        }

        var body = PoracleJsonHelper.RewriteRows(rows, Writable, ("distance", distance));
        return (body, new DistanceUpdateResult(0, areaScoped, placeScoped));
    }

    /// <summary>Why a radius cannot be applied to an alarm with this scope, if it cannot.</summary>
    public static ScopeConflict ConflictOf(bool hasAreas, bool hasPlace, int distance) =>
        (hasAreas, hasPlace, distance) switch
        {
            (true, _, > 0) => ScopeConflict.AreaScoped,
            (false, true, 0) => ScopeConflict.PlaceScoped,
            _ => ScopeConflict.None,
        };

    public enum ScopeConflict
    {
        None,
        AreaScoped,
        PlaceScoped,
    }

    private static void Record(List<int> into, JsonElement row)
    {
        if (PoracleJsonHelper.UidOf(row) is int uid)
        {
            into.Add(uid);
        }
    }

    /// <summary>
    /// Whether the stored row is limited to areas. PoracleNG reads no override back as <c>null</c>; the
    /// JSON-text form is tolerated for the same reason the merge guard tolerates it.
    /// </summary>
    private static bool HasAreas(JsonElement row)
    {
        if (!row.TryGetProperty("override_areas", out var areas))
        {
            return false;
        }

        if (areas.ValueKind == JsonValueKind.String)
        {
            var text = areas.GetString();
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            try
            {
                using var doc = JsonDocument.Parse(text);
                return doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        return areas.ValueKind == JsonValueKind.Array && areas.GetArrayLength() > 0;
    }

    private static bool HasPlace(JsonElement row) =>
        row.TryGetProperty("override_location_label", out var label)
        && label.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(label.GetString());
}
