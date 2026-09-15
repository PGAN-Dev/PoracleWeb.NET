namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>
/// The mute scopes PoracleNG accepts. These strings are the wire contract (they are an enum in
/// upstream's schema), so they live in one place and both the controller's validation and the SPA
/// spell them the same way.
/// </summary>
public static class MuteScopes
{
    public const string Gym = "gym";
    public const string Pokemon = "pokemon";
    public const string Area = "area";
    public const string Pokestop = "pokestop";
    public const string Station = "station";
    public const string Tracking = "tracking";
    public const string Everything = "everything";

    /// <summary>Every scope upstream will return on a read.</summary>
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Gym, Pokemon, Area, Pokestop, Station, Tracking, Everything,
    };

    /// <summary>
    /// The scopes PoracleWeb.NET will CREATE. Reads render all seven -- a mute set from the Discord bot
    /// must be visible and liftable here, or a user wondering why they get nothing has no way to find out.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>tracking</c> is excluded deliberately. Tracking uids are per-table auto-increments whose ranges
    /// overlap (on live data gym uids 31-121 sit wholly inside raid 59-343 and monster 4-36480) and
    /// upstream's matcher compares the uid with no type qualifier, so quieting gym rule 121 would also
    /// quiet raid rule 121. That is an upstream defect, not something a client can paper over.
    /// </para>
    /// <para>
    /// <c>pokestop</c> is excluded because nothing in PoracleWeb.NET names a pokestop: neither the lure
    /// nor the invasion model carries a fort id. <c>everything</c> is excluded because the user menu's
    /// Pause Alerts already owns the account-wide idea.
    /// </para>
    /// </remarks>
    public static readonly IReadOnlySet<string> WritableFromWeb = new HashSet<string>(StringComparer.Ordinal)
    {
        Gym, Pokemon, Area, Station,
    };

    /// <summary>Longest mute PoracleNG will accept, in minutes (one week).</summary>
    public const int MaxDurationMinutes = 10080;

    /// <summary>PoracleNG's own default when a create omits the duration.</summary>
    public const int DefaultDurationMinutes = 60;
}
