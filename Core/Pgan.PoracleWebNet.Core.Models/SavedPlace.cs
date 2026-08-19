using System.ComponentModel.DataAnnotations;

namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>
/// A named coordinate a user can point an alarm at, instead of their profile pin.
/// </summary>
/// <remarks>
/// Stored by PoracleNG in <c>user_locations</c>, keyed by (human, label). Labels are the user's own
/// words — "home", "work" — and are what an alarm's <c>override_location_label</c> refers to.
/// </remarks>
public class SavedPlace
{
    [Required]
    [StringLength(64, MinimumLength = 1)]
    public string Label { get; set; } = string.Empty;

    [Range(-90, 90)]
    public double Latitude
    {
        get; set;
    }

    [Range(-180, 180)]
    public double Longitude
    {
        get; set;
    }
}

/// <summary>
/// Everywhere a user's alarms can be anchored: the profile pin, plus whatever they have named.
/// </summary>
public class SavedPlaces
{
    /// <summary>
    /// The profile pin, which every alarm falls back to. Null when the user has never set a location.
    /// </summary>
    public SavedPlace? Default
    {
        get; set;
    }

    public List<SavedPlace> Named { get; set; } = [];
}

/// <summary>
/// Why a place could not be deleted: the alarms still pointing at it.
/// </summary>
/// <remarks>
/// PoracleNG answers 409 with a <c>referencing_rules</c> list rather than orphaning the label. Worth
/// surfacing rather than flattening into "could not delete": the useful thing to tell someone is which
/// alarms they need to repoint first.
/// </remarks>
public class PlaceInUseException(IReadOnlyList<string> referencingRules)
    : Exception($"That place is still used by {referencingRules.Count} alarm(s).")
{
    public IReadOnlyList<string> ReferencingRules { get; } = referencingRules;
}
