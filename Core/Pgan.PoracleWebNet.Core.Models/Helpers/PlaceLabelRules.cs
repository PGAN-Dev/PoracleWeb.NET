namespace Pgan.PoracleWebNet.Core.Models.Helpers;

/// <summary>
/// The one place that decides whether a new saved-place label is acceptable.
/// </summary>
/// <remarks>
/// Labels were bounded by length only, so "Gym" + U+202E + "evil" was saved and rendered as "Gymlive" (the
/// spoof <see cref="ProfileNameRules"/> exists to stop) and a line break split a label across two lines on
/// every card that names it. The character rule is the profile name's, shared rather than copied.
/// <para>
/// Applied to a <b>new</b> label only, which on this API means adding a place: move and delete address the
/// label already stored and change nothing about it. Production could not be queried when the rule was
/// added, so a label saved before it may carry one of these characters; judging only new labels keeps every
/// such place movable and deletable. PoracleNG answers 409 to an add whose label already exists, so an add
/// never rewrites an old label either.
/// </para>
/// </remarks>
public static class PlaceLabelRules
{
    /// <summary><c>humans_locations.label</c> is varchar(64).</summary>
    public const int MaxLength = 64;

    /// <summary>
    /// Returns the message to report, or <c>null</c> when the label is acceptable. The label is judged trimmed,
    /// which is how it is saved.
    /// </summary>
    public static string? Validate(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return "Give the place a name.";
        }

        var trimmed = label.Trim();

        if (trimmed.Length > MaxLength)
        {
            return $"That name is too long. Use {MaxLength} characters or fewer.";
        }

        return trimmed.Any(ProfileNameRules.IsRefused)
            ? "A place name can't contain line breaks, control or text-direction characters."
            : null;
    }
}
