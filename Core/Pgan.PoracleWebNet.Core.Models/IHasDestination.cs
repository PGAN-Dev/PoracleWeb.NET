using System.Text.Json.Serialization;

namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>
/// An alarm that says which of a linked pair's two humans owns it. Both members are omitted from the
/// JSON when null, so a person with no link gets the body they always got.
/// </summary>
public interface IHasDestination
{
    /// <summary>"discord" or "telegram". Stamped on reads only when the caller is linked.</summary>
    public string? Destination
    {
        get; set;
    }

    /// <summary>True on a create whose target side is paused (stopped, blocked or not registered).</summary>
    public bool? PausedDestination
    {
        get; set;
    }
}
