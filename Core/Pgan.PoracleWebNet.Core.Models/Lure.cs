namespace Pgan.PoracleWebNet.Core.Models;

public class Lure
{
    public int Uid
    {
        get; set;
    }
    public string Id { get; set; } = string.Empty;
    public string? Ping
    {
        get; set;
    }
    public int Distance
    {
        get; set;
    }
    public int LureId
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
}
