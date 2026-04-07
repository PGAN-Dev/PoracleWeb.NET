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
    /// Serializes a value to a JsonElement using snake_case naming.
    /// Strips "uid":0 from the output — PoracleNG treats uid=0 as an update target
    /// instead of a new insert. Omitting uid tells PoracleNG to create a new row.
    /// </summary>
    public static JsonElement SerializeToElement<T>(T value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, SnakeCaseOptions);
        using var doc = JsonDocument.Parse(bytes);
        var root = doc.RootElement;

        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("uid", out var uid) && uid.GetInt32() == 0)
        {
            return StripProperty(root, "uid");
        }

        if (root.ValueKind == JsonValueKind.Array)
        {
            return StripZeroUidsFromArray(root);
        }

        return root.Clone();
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

    private static JsonElement StripZeroUidsFromArray(JsonElement array)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            foreach (var item in array.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("uid", out var uid) && uid.GetInt32() == 0)
                {
                    StripPropertyTo(writer, item, "uid");
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

    private static void StripPropertyTo(Utf8JsonWriter writer, JsonElement obj, string propertyName)
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
