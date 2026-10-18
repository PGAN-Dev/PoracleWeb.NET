using System.Text.Json;

namespace Pgan.PoracleWebNet.Core.Services;

/// <summary>
/// Public door onto <c>JsonElementExtensions.GetAreaListJsonOrNull</c> for callers outside
/// <c>Core.Services</c> (the API's <c>AreaController</c> reads the raw human JSON). The extension stays
/// internal with the rest of the lenient readers; only this one operation is exposed.
/// </summary>
public static class PoracleAreaJson
{
    /// <summary>
    /// Reads <paramref name="name"/> from a PoracleNG object whose area list may be a JSON-encoded string
    /// (5.1.0, 5.2.1) or a real array (jfberry/PoracleNG#303), returning the JSON-encoded string form. Absent or
    /// JSON null is null; any other kind throws <see cref="InvalidOperationException"/>.
    /// </summary>
    public static string? ReadAreaListJson(JsonElement element, string name = "area") =>
        element.GetAreaListJsonOrNull(name);
}
