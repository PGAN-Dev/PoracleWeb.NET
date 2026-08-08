using System.ComponentModel.DataAnnotations;

namespace Pgan.PoracleWebNet.Core.Models;

public class BulkDistanceRequest
{
    public List<int> Uids { get; set; } = [];

    /// <summary>
    /// Metres. Zero means area-based delivery.
    /// </summary>
    /// <remarks>
    /// Every *Create and *Update model has carried <c>[Range(0, int.MaxValue)]</c> on Distance since the
    /// beginning; this one did not, so the bulk endpoints accepted negatives that the create path rejects.
    /// PoracleNG clamps the upper bound but not the lower, and it gates radius matching on
    /// <c>distance &gt; 0</c> — so a negative value silently switched those alarms to area-based delivery
    /// while the card went on showing a negative radius. See #417.
    /// </remarks>
    [Range(0, int.MaxValue)]
    public int Distance
    {
        get; set;
    }
}
