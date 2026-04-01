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
    /// </summary>
    public static JsonElement SerializeToElement<T>(T value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, SnakeCaseOptions);
        using var doc = JsonDocument.Parse(bytes);
        return doc.RootElement.Clone();
    }

    /// <summary>
    /// Deserializes a JsonElement array to a typed list using snake_case naming.
    /// </summary>
    public static List<T> DeserializeList<T>(JsonElement json)
    {
        return json.Deserialize<List<T>>(SnakeCaseOptions) ?? [];
    }
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
