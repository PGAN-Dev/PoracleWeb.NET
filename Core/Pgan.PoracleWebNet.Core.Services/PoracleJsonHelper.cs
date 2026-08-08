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
}
