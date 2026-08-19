using System.Globalization;
using System.Text.Json;

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
            return StripAlarmMetadata(root);
        }

        return root.Clone();
    }

    /// <summary>Names that must never reach PoracleNG on an alarm write. See <see cref="SerializeToElement"/>.</summary>
    private static bool ShouldStrip(JsonProperty prop) =>
        prop.NameEquals("profile_no") ||
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
    /// Rewrites stored rows for a write-back, changing only the named properties and passing every
    /// other property through byte-for-byte.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The bulk paths used to deserialize into the typed alarm model, mutate one field and serialize the
    /// model again. That silently dropped every property PoracleWeb does not model, and because the POST
    /// carries a uid PoracleNG upserts the row — so the dropped values were not orphaned, they were
    /// erased. PoracleNG 5.1.0 added <c>override_location_label</c>, <c>override_areas</c> and
    /// <c>pvp_ranking_evolution</c>; 5.2.0 adds <c>costume</c>. Enumerating them here would only work
    /// until the next one lands, so nothing is enumerated: the stored row is the source of truth and the
    /// caller states the few properties it means to change. See #730.
    /// </para>
    /// <para>
    /// The same uid/profile_no stripping as <see cref="SerializeToElement{T}"/> applies, so a rewritten
    /// row lands on the live active profile rather than a stale one. See #411.
    /// </para>
    /// </remarks>
    /// <param name="rows">The array PoracleNG returned from a tracking read.</param>
    /// <param name="include">Rows to write back. Rows it rejects are left out of the result entirely.</param>
    /// <param name="changes">Properties to set. A name the row does not carry is appended.</param>
    public static JsonElement RewriteRows(
        JsonElement rows,
        Func<JsonElement, bool> include,
        params (string Name, int Value)[] changes)
    {
        if (rows.ValueKind != JsonValueKind.Array)
        {
            return EmptyArray;
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            foreach (var row in rows.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object || !include(row))
                {
                    continue;
                }

                WriteRowWithChanges(writer, row, changes);
            }

            writer.WriteEndArray();
        }

        return JsonDocument.Parse(stream.ToArray()).RootElement.Clone();
    }

    private static void WriteRowWithChanges(
        Utf8JsonWriter writer, JsonElement row, (string Name, int Value)[] changes)
    {
        writer.WriteStartObject();
        var written = new HashSet<string>(StringComparer.Ordinal);

        foreach (var prop in row.EnumerateObject())
        {
            if (ShouldStrip(prop))
            {
                continue;
            }

            var change = Array.FindIndex(changes, c => string.Equals(c.Name, prop.Name, StringComparison.Ordinal));
            if (change >= 0)
            {
                writer.WriteNumber(changes[change].Name, changes[change].Value);
                written.Add(prop.Name);
                continue;
            }

            prop.WriteTo(writer);
        }

        foreach (var (name, value) in changes)
        {
            if (written.Add(name))
            {
                writer.WriteNumber(name, value);
            }
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
    /// Adds back every property the stored row carries that the written body does not, so a single-alarm
    /// edit preserves the fields PoracleWeb has no model for. See <see cref="RewriteRows"/> and #730.
    /// </summary>
    public static JsonElement PreserveUnmodelled(JsonElement stored, JsonElement written)
    {
        if (stored.ValueKind != JsonValueKind.Object || written.ValueKind != JsonValueKind.Object)
        {
            return written;
        }

        var present = new HashSet<string>(StringComparer.Ordinal);
        foreach (var prop in written.EnumerateObject())
        {
            present.Add(prop.Name);
        }

        var missing = stored.EnumerateObject().Where(p => !present.Contains(p.Name) && !ShouldStrip(p)).ToList();
        if (missing.Count == 0)
        {
            return written;
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var prop in written.EnumerateObject())
            {
                prop.WriteTo(writer);
            }

            foreach (var prop in missing)
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
