namespace Pgan.PoracleWebNet.Core.Models;

public class WeatherData
{
    public int Condition
    {
        get; set;
    }
    public string ConditionName { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public List<string> BoostedTypes { get; set; } = [];
    public bool HasWarning
    {
        get; set;
    }
    public int Severity
    {
        get; set;
    }
    public DateTimeOffset? UpdatedAt
    {
        get; set;
    }

    /// <summary>
    /// Maps gameplay_condition int to weather name, icon, and boosted types.
    /// </summary>
    public static WeatherData FromCondition(int condition, int severity = 0, bool warnWeather = false, long? updatedUnix = null)
    {
        var (name, icon, types) = condition switch
        {
            1 => ("Clear", "wb_sunny", ["Fire", "Grass", "Ground"]),
            2 => ("Rainy", "water_drop", ["Water", "Electric", "Bug"]),
            3 => ("Partly Cloudy", "filter_drama", ["Normal", "Rock"]),
            4 => ("Cloudy", "cloud", ["Fairy", "Fighting", "Poison"]),
            5 => ("Windy", "air", ["Dragon", "Flying", "Psychic"]),
            6 => ("Snow", "ac_unit", ["Ice", "Steel"]),
            7 => ("Fog", "foggy", ["Dark", "Ghost"]),
            _ => ("Unknown", "help_outline", Array.Empty<string>()),
        };

        return new WeatherData
        {
            Condition = condition,
            ConditionName = name,
            Icon = icon,
            BoostedTypes = [.. types],
            HasWarning = warnWeather || severity > 0,
            Severity = severity,
            UpdatedAt = updatedUnix.HasValue ? DateTimeOffset.FromUnixTimeSeconds(updatedUnix.Value) : null,
        };
    }
}
