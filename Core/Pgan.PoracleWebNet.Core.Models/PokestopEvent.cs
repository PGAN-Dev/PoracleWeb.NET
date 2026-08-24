namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>
/// One pokestop-event alarm: Showcase, Kecleon or Gold Stop.
/// </summary>
/// <remarks>
/// Upstream calls the tracking type <c>incident</c> and the rule field <c>display_type</c>. Neither
/// word appears in the UI or in a route the browser calls — they live inside
/// <c>IPoracleIncidentProxy</c> and nowhere else.
/// </remarks>
public class PokestopEvent
{
    public int Uid
    {
        get; set;
    }

    public string Id { get; set; } = string.Empty;

    public int ProfileNo
    {
        get; set;
    }

    /// <summary>The pokestop-event id. Required upstream, and it has no wildcard.</summary>
    public int DisplayType
    {
        get; set;
    }

    /// <summary>The stored <c>grunt_type</c> for <see cref="DisplayType"/>, for display only.</summary>
    public string? EventName => PokestopEventTypes.NameFor(this.DisplayType);

    public int Distance
    {
        get; set;
    }

    public string? Template
    {
        get; set;
    }

    /// <summary>The PoracleNG clean bitmask: bit 1 auto-delete, bit 2 edit-in-place, bit 4 summary.</summary>
    public int Clean
    {
        get; set;
    }

    /// <summary>Saved-place label this alarm measures its radius from, instead of the profile's pin.</summary>
    public string? OverrideLocationLabel
    {
        get; set;
    }

    /// <summary>Areas this alarm is confined to, instead of the profile's area list.</summary>
    public List<string>? OverrideAreas
    {
        get; set;
    }
}
