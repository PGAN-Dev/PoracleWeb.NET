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
/// <see cref="Details"/> is what the comparable form drops but a person needs to read the line (profile names,
/// place labels as typed). It is outside the fingerprint, so renaming a profile does not stale an unrelated line.
/// </summary>
public sealed record DriftItem(DriftField Field, JsonElement Discord, JsonElement Telegram, string Fingerprint, JsonElement? Details = null);

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

/// <summary>
/// A resolve that cannot go ahead. <see cref="Code"/> is what the client translates; the message is the English
/// fallback and what is logged. <see cref="Side"/>, <see cref="Name"/> and <see cref="Count"/> fill the sentence.
/// </summary>
public abstract class DriftRefusalException(string message, Exception? inner = null) : Exception(message, inner)
{
    public abstract string Code { get; }

    public virtual LinkSide? Side => null;

    public virtual string? Name => null;

    public virtual int? Count => null;

    protected static string App(LinkSide side) => side == LinkSide.Discord ? "Discord" : "Telegram";

    protected static string Called(int no, string? name) =>
        string.IsNullOrWhiteSpace(name) ? $"profile {no.ToString(System.Globalization.CultureInfo.InvariantCulture)}" : name;
}

/// <summary>The pair changed between the list being drawn and the person's choice.</summary>
public sealed class DriftChangedException() : DriftRefusalException("That changed since the list was drawn. Look at it again, then choose.")
{
    public override string Code => "drift_changed";
}

/// <summary>A profile the kept side lacks still holds alarms on the other side, so it is not deleted.</summary>
public sealed class DriftProfileHoldsAlarmsException(LinkSide side, int profileNo, string? name, int count)
    : DriftRefusalException($"{App(side)}'s profile {Called(profileNo, name)} has {count} alarm(s), so it can't be removed. Delete those alarms in {App(side)} first, or keep {App(side)}'s profiles. Nothing was changed.")
{
    public override string Code => "profile_holds_alarms";

    public override LinkSide? Side => side;

    public override string? Name => Called(profileNo, name);

    public override int? Count => count;

    public int ProfileNo { get; } = profileNo;

    public string? ProfileName { get; } = name;
}

/// <summary>A saved place the kept side lacks is still aimed at by alarms on the other side, so it is not deleted.</summary>
public sealed class DriftPlaceInUseException(LinkSide side, string label, int count)
    : DriftRefusalException($"{App(side)}'s saved place \"{label}\" is used by {count} alarm(s), so it can't be removed. Change those alarms in {App(side)} first, or keep {App(side)}'s places. Nothing was changed.")
{
    public override string Code => "place_in_use";

    public override LinkSide? Side => side;

    public override string? Name => label;

    public override int? Count => count;

    public string Label { get; } = label;
}

/// <summary>
/// The other side has no profile at the number the kept side is on, so it cannot be switched to it. The profiles
/// difference has to be settled first; switching never creates a profile.
/// </summary>
public sealed class DriftProfileMissingException(LinkSide side, int profileNo, string? name)
    : DriftRefusalException($"{App(side)} has no profile {Called(profileNo, name)} yet. Match the profiles first, then the active profile. Nothing was changed.")
{
    public override string Code => "profile_missing";

    public override LinkSide? Side => side;

    public override string? Name => Called(profileNo, name);

    public int ProfileNo { get; } = profileNo;
}

/// <summary>A resolve failed after some of its writes had landed; the person must look again.</summary>
public sealed class DriftPartiallyAppliedException(Exception inner)
    : DriftRefusalException("Some changes were made before this failed. Look at the differences on Linked accounts again.", inner)
{
    public override string Code => "partially_applied";
}
