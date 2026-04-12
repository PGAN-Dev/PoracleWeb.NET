using System.Text.Json;

namespace Pgan.PoracleWebNet.Core.Services.TestAlerts;

/// <summary>
/// Helpers to extract filter values from a PoracleNG alarm row (returned as JSON).
/// All accessors are forgiving: they accept both string-encoded numbers and native JSON numbers,
/// and return the supplied default when the field is missing or an unexpected type.
/// </summary>
internal static class FilterFieldExtensions
{
    public static int GetInt(this JsonElement element, string property, int defaultValue)
    {
        if (!element.TryGetProperty(property, out var prop))
        {
            return defaultValue;
        }

        // IDE0072 off: the wildcard arm is the intentional fall-through for every other
        // JsonValueKind (undefined/object/array/bool/null). dotnet format insists on
        // expanding these to explicit throws — not what we want for defensive parsing.
#pragma warning disable IDE0072
        return prop.ValueKind switch
        {
            JsonValueKind.Number when prop.TryGetInt32(out var val) => val,
            JsonValueKind.String when int.TryParse(prop.GetString(), out var parsed) => parsed,
            _ => defaultValue,
        };
#pragma warning restore IDE0072
    }

    public static double GetDouble(this JsonElement element, string property, double defaultValue)
    {
        if (!element.TryGetProperty(property, out var prop))
        {
            return defaultValue;
        }

#pragma warning disable IDE0072
        return prop.ValueKind switch
        {
            JsonValueKind.Number when prop.TryGetDouble(out var val) => val,
            JsonValueKind.String when double.TryParse(prop.GetString(), out var parsed) => parsed,
            _ => defaultValue,
        };
#pragma warning restore IDE0072
    }

    public static string GetString(this JsonElement element, string property, string defaultValue)
    {
        if (element.TryGetProperty(property, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            return prop.GetString() ?? defaultValue;
        }

        return defaultValue;
    }

    /// <summary>
    /// Pick a value inside a <c>[min, max]</c> range, preferring <paramref name="preferred"/>
    /// when it sits inside the range, otherwise the range's midpoint, clamped to the range.
    /// Used to synthesize IV/level/CP values that satisfy filter ranges.
    /// </summary>
    public static int PickInRange(int min, int max, int preferred)
    {
        if (min > max)
        {
            (min, max) = (max, min);
        }

        if (preferred >= min && preferred <= max)
        {
            return preferred;
        }

        return (min + max) / 2;
    }

    /// <summary>Half-step version of <see cref="PickInRange(int,int,int)"/> for levels.</summary>
    public static double PickLevelInRange(double min, double max, double preferred)
    {
        if (min > max)
        {
            (min, max) = (max, min);
        }

        if (preferred >= min && preferred <= max)
        {
            return preferred;
        }

        // Round midpoint to the nearest 0.5 half-step so it's a legal level.
        var mid = (min + max) / 2.0;
        return Math.Round(mid * 2.0) / 2.0;
    }
}
