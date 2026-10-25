using System.ComponentModel.DataAnnotations;

namespace Pgan.PoracleWebNet.Core.Models;

public class BulkDistanceRequest
{
    public List<int> Uids { get; set; } = [];

    /// <summary>
    /// For the invasion list only: "event" or "invasion", the kind of the cards whose radius changes. Invasion and
    /// Pokestop event rules are numbered separately, so a uid alone can name either. Absent or unknown keeps the
    /// uid-only behaviour.
    /// </summary>
    public string? Kind { get; set; }

    /// <summary>
    /// Metres. Zero means area-based delivery.
    /// </summary>
    /// <remarks>
    /// Every *Create and *Update model has carried <c>[Range(0, int.MaxValue)]</c> on Distance since the
    /// beginning; this one did not, so the bulk endpoints accepted negatives that the create path rejects.
    /// PoracleNG clamps the upper bound but not the lower, and it gates radius matching on
    /// <c>distance &gt; 0</c> — so a negative value silently switched those alarms to area-based delivery
    /// while the card went on showing a negative radius. See #417. The upper bound is half the Earth's
    /// circumference (<see cref="AlarmDistance.MaxMetres"/>), the same as every alarm DTO.
    /// </remarks>
    [Range(0, AlarmDistance.MaxMetres)]
    public int Distance
    {
        get; set;
    }
}
