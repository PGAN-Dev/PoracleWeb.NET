namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>
/// Helpers for the PoracleNG alarm <c>clean</c> column, which is a 3-bit bitmask:
/// bit 1 = auto-delete, bit 2 = edit-in-place, bit 4 = summary. Mirrors PoracleNG's
/// <c>db.IsClean</c> / <c>db.IsEdit</c> / <c>db.IsSummary</c> (processor/internal/db/clean.go),
/// so reads and writes preserve bits the web UI does not surface.
/// </summary>
public static class CleanFlags
{
    /// <summary>Auto-delete bit (bit 1). PoracleNG <c>db.IsClean</c>.</summary>
    public const int AutoDelete = 1;

    /// <summary>Edit-in-place bit (bit 2). PoracleNG <c>db.IsEdit</c>.</summary>
    public const int Edit = 2;

    /// <summary>Summary bit (bit 4). PoracleNG <c>db.IsSummary</c>.</summary>
    public const int Summary = 4;

    /// <summary>All known bits combined (7).</summary>
    public const int All = AutoDelete | Edit | Summary;

    /// <summary>True when the auto-delete bit (bit 1) is set.</summary>
    public static bool IsAutoDelete(int clean) => (clean & AutoDelete) != 0;

    /// <summary>True when the edit-in-place bit (bit 2) is set.</summary>
    public static bool IsEdit(int clean) => (clean & Edit) != 0;

    /// <summary>True when the summary bit (bit 4) is set.</summary>
    public static bool IsSummary(int clean) => (clean & Summary) != 0;

    /// <summary>Composes a clean bitmask from the three known flags.</summary>
    public static int Compose(bool autoDelete, bool edit, bool summary) =>
        (autoDelete ? AutoDelete : 0) | (edit ? Edit : 0) | (summary ? Summary : 0);

    /// <summary>
    /// Returns <paramref name="existing"/> with only the bits in <paramref name="mask"/>
    /// replaced by the corresponding bits from <paramref name="changes"/>. Bits outside the
    /// mask are left untouched, so bot-set bits the web UI does not edit survive a save.
    /// </summary>
    public static int Preserve(int existing, int mask, int changes) => (existing & ~mask) | (changes & mask);
}
