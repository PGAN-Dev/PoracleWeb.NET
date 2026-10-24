using System.Globalization;
using System.Text.Json;
using Pgan.PoracleWebNet.Core.Models.Helpers;

namespace Pgan.PoracleWebNet.Core.Services;

/// <summary>
/// Shared JSON serialization helpers for PoracleNG API proxy services.
/// All PoracleNG API communication uses snake_case JSON naming.
/// </summary>
internal static class PoracleJsonHelper
{
    public static readonly JsonSerializerOptions SnakeCaseOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Cached empty JSON array element — avoids allocating a new JsonDocument on every empty response.
    /// </summary>
    public static readonly JsonElement EmptyArray = JsonDocument.Parse("[]").RootElement.Clone();

    /// <summary>
    /// Serializes an alarm payload for PoracleNG, using snake_case naming and removing two properties
    /// that do more harm than good on the wire.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>uid: 0</c> is stripped because PoracleNG reads a present uid as "update this row", so a new
    /// alarm with the default uid became an update against a row that does not exist instead of an insert.
    /// </para>
    /// <para>
    /// <c>profile_no</c> is stripped because it was stamped from the caller's JWT claim, and that claim
    /// goes stale whenever <c>current_profile_no</c> changes out of band — the active-hours scheduler, the
    /// bot's <c>!profile</c> command, or a second tab. PoracleNG honours a submitted <c>profile_no</c>
    /// verbatim for the pokemon type while scoping every other type, and every read path, to the live
    /// <c>current_profile_no</c>. A stale claim therefore wrote a monster onto a profile the user was no
    /// longer on: the POST returned 201 with a real uid, and the row was then invisible to reads and
    /// undeletable. Confirmed against PoracleNG that a submitted <c>profile_no</c> is taken at face value
    /// even when no such profile exists — <c>profile_no: 9</c> creates an orphan — and that omitting it
    /// makes PoracleNG use <c>current_profile_no</c>. Omitting it is therefore both the safe option and
    /// the one that matches how the other nine types already behave. See #411.
    /// </para>
    /// </remarks>
    public static JsonElement SerializeToElement<T>(T value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, SnakeCaseOptions);
        using var doc = JsonDocument.Parse(bytes);
        var root = doc.RootElement;

        if (root.ValueKind == JsonValueKind.Array)
        {
            return StripAlarmMetadataFromArray(root);
        }

        if (root.ValueKind == JsonValueKind.Object)
        {
            var body = StripAlarmMetadata(root);

            // A move attaches the stored properties its typed model has no field for.
            return value is not null && CarriedFields.TryGet(value, out var carried)
                ? PreserveUnmodelled(carried, body)
                : body;
        }

        return root.Clone();
    }

    /// <summary>Names that must never reach PoracleNG on an alarm write. See <see cref="SerializeToElement"/>.</summary>
    /// <remarks>
    /// <c>description</c> is PoracleNG's own rendering of the rule, handed back on every read. No
    /// tracking table has a column for it, so sending it back is at best ignored -- and it travels on
    /// more than one write path, because <see cref="PreserveUnmodelled"/> copies stored properties
    /// through verbatim. Stripping it here covers them all at once. See #810.
    /// </remarks>
    private static bool ShouldStrip(JsonProperty prop) =>
        prop.NameEquals("profile_no") ||
        // PoracleWeb.NET's own annotations for a linked pair; no tracking table has either.
        prop.NameEquals("destination") ||
        prop.NameEquals("paused_destination") ||
        prop.NameEquals("description") ||
        (prop.NameEquals("uid") && prop.Value.ValueKind == JsonValueKind.Number && prop.Value.GetInt32() == 0);

    private static JsonElement StripAlarmMetadata(JsonElement obj)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            WriteStripped(writer, obj);
        }

        return JsonDocument.Parse(stream.ToArray()).RootElement.Clone();
    }

    /// <summary>
    /// Removes a named property from a JSON object, returning a new JsonElement without it.
    /// </summary>
    public static JsonElement StripProperty(JsonElement obj, string propertyName)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var prop in obj.EnumerateObject())
            {
                if (prop.NameEquals(propertyName))
                {
                    continue;
                }

                prop.WriteTo(writer);
            }

            writer.WriteEndObject();
        }

        return JsonDocument.Parse(stream.ToArray()).RootElement.Clone();
    }

    private static JsonElement StripAlarmMetadataFromArray(JsonElement array)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            foreach (var item in array.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object)
                {
                    WriteStripped(writer, item);
                }
                else
                {
                    item.WriteTo(writer);
                }
            }

            writer.WriteEndArray();
        }

        return JsonDocument.Parse(stream.ToArray()).RootElement.Clone();
    }

    private static void WriteStripped(Utf8JsonWriter writer, JsonElement obj)
    {
        writer.WriteStartObject();
        foreach (var prop in obj.EnumerateObject())
        {
            if (ShouldStrip(prop))
            {
                continue;
            }

            prop.WriteTo(writer);
        }

        writer.WriteEndObject();
    }


    /// <summary>
    /// Reads the uid of a row PoracleNG returned, or null when it carries none.
    /// </summary>
    public static int? UidOf(JsonElement row) =>
        row.ValueKind == JsonValueKind.Object
        && row.TryGetProperty("uid", out var uid)
        && uid.ValueKind == JsonValueKind.Number
            ? uid.GetInt32()
            : null;

    /// <summary>
    /// Adds back every property the stored row carries that the written body does not state, so a
    /// single-alarm edit preserves fields the caller had no value for. See #730.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A <c>null</c> in the written body counts as "not stated", not as "clear this". Once the models
    /// gained <c>OverrideLocationLabel</c> and <c>OverrideAreas</c>, every serialized alarm carried them
    /// as explicit nulls whether or not the caller had read them, and treating that as a value meant an
    /// edit wiped the stored override — the exact defect this helper exists to prevent, reintroduced by
    /// modelling the field.
    /// </para>
    /// <para>
    /// Clearing is still expressible, and matches what PoracleNG reads as empty: an empty array for
    /// <c>override_areas</c> (<c>normalizeOverrideAreas</c> maps it to nil) and an empty string for
    /// <c>override_location_label</c> (<c>nullIfEmpty</c>). Same null-versus-empty split the models
    /// already use for <c>gym_id</c>. See #730.
    /// </para>
    /// </remarks>
    public static JsonElement PreserveUnmodelled(JsonElement stored, JsonElement written)
    {
        if (stored.ValueKind != JsonValueKind.Object || written.ValueKind != JsonValueKind.Object)
        {
            return written;
        }

        var stated = new HashSet<string>(StringComparer.Ordinal);
        foreach (var prop in written.EnumerateObject())
        {
            if (prop.Value.ValueKind != JsonValueKind.Null)
            {
                stated.Add(prop.Name);
            }
        }

        var fromStored = stored.EnumerateObject()
            .Where(p => !stated.Contains(p.Name) && !ShouldStrip(p))
            .ToList();

        if (fromStored.Count == 0)
        {
            return written;
        }

        var supplied = new HashSet<string>(fromStored.Select(p => p.Name), StringComparer.Ordinal);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var prop in written.EnumerateObject())
            {
                // The stored row is about to supply this one; writing the null too would duplicate the key.
                if (supplied.Contains(prop.Name))
                {
                    continue;
                }

                prop.WriteTo(writer);
            }

            foreach (var prop in fromStored)
            {
                prop.WriteTo(writer);
            }

            writer.WriteEndObject();
        }

        return JsonDocument.Parse(stream.ToArray()).RootElement.Clone();
    }

    /// <summary>
    /// Deserializes a JsonElement array to a typed list using snake_case naming.
    /// </summary>
    public static List<T> DeserializeList<T>(JsonElement json) => json.Deserialize<List<T>>(SnakeCaseOptions) ?? [];
}

/// <summary>
/// JsonElement helper extensions for snake_case property access.
/// </summary>
internal static class JsonElementExtensions
{
    public static string GetStringProp(this JsonElement el, string name) =>
        el.TryGetProperty(name, out var prop) ? prop.GetString() ?? string.Empty : string.Empty;

    public static string? GetStringPropOrNull(this JsonElement el, string name) =>
        el.TryGetProperty(name, out var prop) && prop.ValueKind != JsonValueKind.Null ? prop.GetString() : null;

    public static int GetIntProp(this JsonElement el, string name) =>
        el.TryGetProperty(name, out var prop) && prop.TryGetInt32(out var val) ? val : 0;

    public static double GetDoubleProp(this JsonElement el, string name) =>
        el.TryGetProperty(name, out var prop) && prop.TryGetDouble(out var val) ? val : 0.0;

    /// <summary>
    /// Reads a flag PoracleNG may send as a real JSON boolean or as a v1-style 0/1 integer. Released servers
    /// (5.1.0, 5.2.1) send the integer from <c>GET /api/v2/humans/{id}</c> and the boolean from the list
    /// <c>GET /api/v2/humans</c>; since jfberry/PoracleNG#303 the single-human route sends booleans too.
    /// Both shapes must read, with no version gate.
    /// </summary>
    public static int GetBoolAsIntProp(this JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var prop))
        {
            return 0;
        }

        return prop.ValueKind switch
        {
            JsonValueKind.True => 1,
            JsonValueKind.False => 0,
            JsonValueKind.Number when prop.TryGetInt32(out var val) => val,
            _ => 0,
        };
    }

    /// <summary>
    /// Reads an area list PoracleNG sends in one of two shapes: a JSON-encoded string
    /// (<c>"[\"a\",\"b\"]"</c>, what 5.1.0 and 5.2.1 send) or a real array (<c>["a","b"]</c>, what
    /// <c>GET /api/v2/humans/{id}</c> sends since jfberry/PoracleNG#303). Returns the JSON-encoded string
    /// form the models store, so nothing downstream changes: a string is returned untouched and an array is
    /// written with <see cref="AreaListJson.Serialize"/>, the same writer every other area column goes
    /// through, so non-ASCII names are escaped (<c>Café</c>) exactly as they are everywhere else and
    /// parse back to the real name. Absent or JSON null is null. Any other kind throws, like the strict
    /// readers, rather than quietly reading as "no areas".
    /// </summary>
    public static string? GetAreaListJsonOrNull(this JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var prop))
        {
            return null;
        }

        switch (prop.ValueKind)
        {
            case JsonValueKind.Null:
                return null;
            case JsonValueKind.String:
                return prop.GetString();
            case JsonValueKind.Array:
                var areas = new List<string>();
                foreach (var item in prop.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String)
                    {
                        throw new InvalidOperationException(
                            $"'{name}' must be a JSON string or an array of strings, but it holds a {item.ValueKind} element.");
                    }

                    areas.Add(item.GetString()!);
                }

                return AreaListJson.Serialize(areas);
            default:
                throw new InvalidOperationException(
                    $"'{name}' must be a JSON string or an array of strings, but it is a {prop.ValueKind}.");
        }
    }

    /// <summary>
    /// Reads a timestamp PoracleNG may send as null, as an empty string, or not at all.
    /// </summary>
    public static DateTime? GetDateTimePropOrNull(this JsonElement el, string name) =>
        el.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String
        && DateTime.TryParse(
            prop.GetString(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
            out var parsed)
            ? parsed
            : null;
}
