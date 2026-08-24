namespace Pgan.PoracleWebNet.Core.Models;

public class Raid
{
    public int Uid
    {
        get; set;
    }
    public string Id { get; set; } = string.Empty;
    public int PokemonId
    {
        get; set;
    }
    public string? Ping
    {
        get; set;
    }
    public int Distance
    {
        get; set;
    }
    public int Team { get; set; } = 4;
    public int Level
    {
        get; set;
    }
    public int Form
    {
        get; set;
    }
    public int Clean
    {
        get; set;
    }
    public string? Template
    {
        get; set;
    }
    public int Move { get; set; } = 9000;
    public int Evolution { get; set; } = 9000;
    public int Exclusive
    {
        get; set;
    }
    public string? GymId
    {
        get; set;
    }
    public int RsvpChanges
    {
        get; set;
    }
    public int ProfileNo
    {
        get; set;
    }

    /// <summary>
    /// Saved-place label this alarm measures its radius from, instead of the profile's pin.
    /// </summary>
    /// <remarks>
    /// Mutually exclusive with <see cref="OverrideAreas"/>, and meaningless without a distance —
    /// PoracleNG refuses both combinations. A label that no longer exists is not an error: PoracleNG
    /// falls through to the profile pin, so deleting a place widens its alarms rather than breaking them.
    /// </remarks>
    public string? OverrideLocationLabel
    {
        get; set;
    }

    /// <summary>
    /// Areas this alarm is confined to, instead of the profile's area list.
    /// </summary>
    /// <remarks>
    /// Replaces the profile's areas outright rather than intersecting with them, and is mutually
    /// exclusive with a distance. Names are lowercase with spaces, matching the geofence convention.
    /// </remarks>
    public List<string>? OverrideAreas
    {
        get; set;
    }
    /// <summary>
    /// The sentence PoracleNG renders for this rule, in the user's alert language.
    /// </summary>
    /// <remarks>
    /// Read-only, and read-only in both directions. PoracleNG returns it on every v1 per-type tracking
    /// read with no query parameter asked for -- verified live against 5.1.0 and 5.2.1 -- and there is no
    /// <c>description</c> column on any of the ten tracking tables, so it is rendered from the other
    /// fields on the way out and means nothing on the way in. <c>PoracleJsonHelper.ShouldStrip</c>
    /// therefore removes it from every write body. A PoracleNG too old to send it leaves this null.
    /// </remarks>
    public string? Description
    {
        get; set;
    }
}
