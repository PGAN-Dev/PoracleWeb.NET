namespace Pgan.PoracleWebNet.Core.Models.Helpers;

/// <summary>
/// The one place that decides whether a profile name is acceptable.
/// </summary>
/// <remarks>
/// Creating a profile checked the name against the <c>profiles.name</c> varchar(255) column and answered a
/// clear 400 (#467). Neither duplicate endpoint repeated the check, so the same name reached the database
/// and came back as an opaque 500 — and the Profile Overview page's duplicate prompt is a free-text input
/// with no maxlength, prefilled with "&lt;source&gt; (Copy)", so a long name is an ordinary thing to type.
/// Shared rather than copied a fourth time. See #504, #519.
/// </remarks>
public static class ProfileNameRules
{
    public const int MaxLength = 255;

    /// <summary>
    /// Returns the message to report, or <c>null</c> when the name is acceptable.
    /// </summary>
    public static string? Validate(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Profile name is required.";
        }

        if (name.Trim().Length > MaxLength)
        {
            return $"Profile name must be {MaxLength} characters or fewer.";
        }

        return name.Any(IsRefused)
            ? "Profile name must not contain control or text-direction characters."
            : null;
    }

    /// <summary>
    /// Control characters, line and paragraph separators, and the Unicode bidirectional controls.
    /// </summary>
    /// <remarks>
    /// Names were stored verbatim, so NUL, TAB and line feeds reached every surface that renders a name,
    /// and U+202E made "evil" display as "live" -- a name that reads as something other than what it is.
    /// Production held none of these when the rule was added (0 of 457 names).
    /// <para>
    /// Deliberately a list of what cannot belong in a name rather than of what can, and deliberately not
    /// "every format character": emoji sequences are joined with U+200D, which is one, so refusing the
    /// whole category would refuse a family emoji.
    /// </para>
    /// </remarks>
    private static bool IsRefused(char c) =>
        char.IsControl(c)
        || c is '\u2028' or '\u2029'
        || c is '\u061C' or '\u200E' or '\u200F'
        || c is >= '\u202A' and <= '\u202E'
        || c is >= '\u2066' and <= '\u2069';
}
