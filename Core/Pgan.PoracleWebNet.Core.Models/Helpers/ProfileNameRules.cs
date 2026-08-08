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

        return name.Trim().Length > MaxLength
            ? $"Profile name must be {MaxLength} characters or fewer."
            : null;
    }
}
