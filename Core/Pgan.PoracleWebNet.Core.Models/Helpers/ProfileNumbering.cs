namespace Pgan.PoracleWebNet.Core.Models.Helpers;

/// <summary>
/// Works out which profile number PoracleNG actually assigned to a newly created profile.
/// </summary>
/// <remarks>
/// <para>
/// PoracleWeb used to predict the number as <c>max(existing) + 1</c>. PoracleNG assigns the
/// <em>lowest free</em> number instead (verified: with profiles 0, 1, 3 a new profile is created at 2),
/// so any user who had ever deleted a non-last profile got a different number than PoracleWeb expected.
/// The create then re-read the wrong number and returned an empty body, and duplicate copied the alarms
/// to a profile number with no profile row — orphaned alarms that later attached themselves to whatever
/// profile was eventually created at that number. See #407.
/// </para>
/// <para>
/// The number is resolved by diffing the profile list either side of the create, rather than by matching
/// on name, because names are not unique — two profiles may legitimately share one.
/// </para>
/// </remarks>
public static class ProfileNumbering
{
    /// <summary>
    /// The profile number that appeared between <paramref name="before"/> and <paramref name="after"/>.
    /// </summary>
    /// <param name="name">
    /// Used only to disambiguate if more than one profile appeared, which would mean something else
    /// created a profile concurrently.
    /// </param>
    /// <returns>The new profile number, or <c>null</c> if none appeared — the create silently failed.</returns>
    public static int? ResolveCreated(
        IEnumerable<Profile> before,
        IEnumerable<Profile> after,
        string? name = null)
    {
        var existing = before.Select(p => p.ProfileNo).ToHashSet();
        var added = after.Where(p => !existing.Contains(p.ProfileNo)).ToList();

        if (added.Count == 0)
        {
            return null;
        }

        if (added.Count == 1)
        {
            return added[0].ProfileNo;
        }

        // A concurrent create. Prefer one matching the name we asked for; failing that the lowest, which
        // is the one PoracleNG would have assigned first.
        var named = added.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal));
        return named?.ProfileNo ?? added.Min(p => p.ProfileNo);
    }
}
