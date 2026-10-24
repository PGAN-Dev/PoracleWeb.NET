using System.Text.Json;

namespace Pgan.PoracleWebNet.Core.Models;

public enum DriftField
{
    Location,
    Areas,
    CurrentProfile,
    Profiles,
    Places,
}

/// <summary>
/// One setting on which a linked pair's two humans differ. Each side's value is its comparable form;
/// <see cref="Fingerprint"/> is a SHA-256 over both, so a resolve can tell the person chose against what they saw.
/// </summary>
public sealed record DriftItem(DriftField Field, JsonElement Discord, JsonElement Telegram, string Fingerprint);

public static class DriftFields
{
    public static string Wire(DriftField field) => field switch
    {
        DriftField.Location => "location",
        DriftField.Areas => "areas",
        DriftField.CurrentProfile => "current_profile",
        DriftField.Profiles => "profiles",
        DriftField.Places => "places",
        _ => throw new ArgumentOutOfRangeException(nameof(field)),
    };

    public static bool TryParse(string? value, out DriftField field)
    {
        foreach (var candidate in Enum.GetValues<DriftField>())
        {
            if (string.Equals(Wire(candidate), value, StringComparison.OrdinalIgnoreCase))
            {
                field = candidate;
                return true;
            }
        }

        field = default;
        return false;
    }
}

/// <summary>The pair changed between the banner being drawn and the person's choice.</summary>
public sealed class DriftChangedException() : Exception("That changed again. Check the banner.");

/// <summary>A profile the kept side lacks still holds alerts on the other side, so it is not deleted.</summary>
public sealed class DriftProfileHoldsAlarmsException(int profileNo, string? name, int count)
    : Exception($"Profile {(string.IsNullOrWhiteSpace(name) ? profileNo.ToString(System.Globalization.CultureInfo.InvariantCulture) : name)} on the other account holds {count} alert(s). Move or delete them there first; nothing was changed.")
{
    public int ProfileNo { get; } = profileNo;

    public string? ProfileName { get; } = name;

    public int Count { get; } = count;
}
