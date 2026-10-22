namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>
/// How many of one alarm type's rules have auto-delete on, out of how many there are. The cleaning
/// status booleans say only "all of them"; this is what lets the page say "1 of 3".
/// </summary>
public sealed record CleanCount(int Clean, int Total)
{
    /// <summary>
    /// True when the type has rules and every one of them is clean. A type with no rules is not clean:
    /// there is nothing to be clean, and an array holding only somebody else's rows is that case too.
    /// </summary>
    public bool IsAllClean() => this.Total > 0 && this.Clean == this.Total;
}
