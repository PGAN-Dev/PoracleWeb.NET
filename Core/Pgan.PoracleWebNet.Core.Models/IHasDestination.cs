using System.Text.Json.Serialization;

namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>
/// An alarm that says which of a linked pair's two humans owns it. Both members are omitted from the
/// JSON when null, so a person with no link gets the body they always got.
/// </summary>
public interface IHasDestination
{
    /// <summary>PoracleNG's id for the rule; lets the router map a uid to its owner from one list read.</summary>
    public int Uid
    {
        get;
    }

    /// <summary>"discord" or "telegram". Stamped on reads only when the caller is linked.</summary>
    public string? Destination
    {
        get; set;
    }

    /// <summary>True on a create whose target side is paused (stopped or blocked).</summary>
    public bool? PausedDestination
    {
        get; set;
    }

    /// <summary>The profile the rule lives on; a move needs it to match the target human's active profile.</summary>
    public int ProfileNo
    {
        get;
    }

    /// <summary>The saved place the rule measures from, which must also exist on a move's target human.</summary>
    public string? OverrideLocationLabel
    {
        get;
    }

    /// <summary>Areas the rule is limited to, which a move's target human must be allowed to name.</summary>
    public List<string>? OverrideAreas
    {
        get;
    }
}
