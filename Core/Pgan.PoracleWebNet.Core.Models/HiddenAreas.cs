using System.Text.Json;

namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>
/// The admin areas an operator has taken off the menu: staging fences, test polygons, regions they
/// cover but do not advertise.
/// </summary>
/// <remarks>
/// <para>
/// Stored as a JSON array of lowercase names in the <c>hidden_areas</c> site setting, and applied in
/// <c>GeofenceFeedController</c> by serving those fences with <c>userSelectable: false</c>.
/// </para>
/// <para>
/// That one flag is the whole mechanism, because PoracleWeb.NET is the geofence source: Poracle loads
/// the feed, its <c>setAreas</c> intersects a non-admin's submission against <c>userSelectable=true</c>
/// fences, the bot's area picker filters on the same field, and this site's own
/// <c>GET /api/areas/available</c> already drops what is not selectable (#544). So one write here
/// reaches every surface without PoracleWeb.NET holding Koji credentials, and an operator who would
/// rather set <c>isPublic</c> in Koji still gets the same result.
/// </para>
/// <para>
/// What it deliberately does NOT do is unsubscribe anyone. Matching never consults
/// <c>userSelectable</c> — <c>resolveOverride</c> hands a rule's areas to <c>areaOverlap</c>, which
/// compares names against the fences a spawn fell in — so a profile already carrying a hidden name
/// keeps receiving alerts from it. Hiding removes an area from the pickers, not from anyone's profile.
/// The admin page says so out loud; see #885.
/// </para>
/// </remarks>
public static class HiddenAreas
{
    /// <summary>The site setting holding the list. Admin-only: no ordinary user has a use for it.</summary>
    public const string SettingKey = "hidden_areas";

    /// <summary>Generous enough for a large instance, bounded so one bad write cannot be unbounded.</summary>
    public const int MaxEntries = 500;

    /// <summary>Poracle matches area names case-sensitively and stores them lowercased, so we do too.</summary>
    public static string Normalize(string name) => name.Trim().ToLowerInvariant();

    /// <summary>
    /// The stored names, or an empty set when nothing is stored or the value cannot be read.
    /// </summary>
    /// <remarks>
    /// Failing to an empty set is deliberate. This runs inside the geofence feed, which PoracleJS
    /// caches as its last good answer; a row we cannot parse must leave every area visible rather than
    /// hide the lot, because the second failure is silent and takes alerting with it.
    /// </remarks>
    public static HashSet<string> Parse(string? raw)
    {
        var hidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return hidden;
        }

        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            root = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return hidden;
        }

        if (root.ValueKind != JsonValueKind.Array)
        {
            return hidden;
        }

        foreach (var entry in root.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var name = entry.GetString();
            if (!string.IsNullOrWhiteSpace(name))
            {
                hidden.Add(Normalize(name));
            }

            if (hidden.Count == MaxEntries)
            {
                break;
            }
        }

        return hidden;
    }

    /// <summary>Serializes a list for storage: normalized, de-duplicated, ordered so diffs stay readable.</summary>
    public static string Serialize(IEnumerable<string> names)
    {
        var ordered = names
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(Normalize)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.Ordinal)
            .Take(MaxEntries);

        return JsonSerializer.Serialize(ordered);
    }

    /// <summary>
    /// Validates a value on the way into <c>site_settings</c>.
    /// </summary>
    /// <remarks>
    /// Bounds the shape rather than the contents. An allowlist of known area names would refuse a fence
    /// Koji has not served yet, and would turn a Koji outage into an unwritable setting.
    /// </remarks>
    public static bool TryValidate(string? value, out string error)
    {
        error = string.Empty;

        // Absent or empty means nothing is hidden, which must stay writable: it is how an operator
        // clears the list.
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(value);
            root = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            error = $"{SettingKey} must be a JSON array of area names.";
            return false;
        }

        if (root.ValueKind != JsonValueKind.Array)
        {
            error = $"{SettingKey} must be a JSON array of area names.";
            return false;
        }

        if (root.GetArrayLength() > MaxEntries)
        {
            error = $"{SettingKey} may hold at most {MaxEntries} names.";
            return false;
        }

        foreach (var entry in root.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(entry.GetString()))
            {
                error = $"Every {SettingKey} entry must be a non-empty area name.";
                return false;
            }

            if (entry.GetString()!.Length > 200)
            {
                error = $"Every {SettingKey} entry must be 200 characters or fewer.";
                return false;
            }
        }

        return true;
    }
}
