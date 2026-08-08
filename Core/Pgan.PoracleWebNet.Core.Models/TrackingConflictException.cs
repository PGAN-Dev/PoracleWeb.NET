namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>
/// The write was refused because it collides with an alarm that already exists.
/// </summary>
/// <remarks>
/// PoracleNG dedups on a per-type natural key. When an edit moves an alarm onto a key another alarm
/// already holds, PoracleNG declines to write and answers 200 with <c>alreadyPresent</c> set. PoracleWeb
/// used to return that as success while echoing the requested values back from its in-memory model, so
/// the response could not disagree with the request and the user believed the edit had applied. For the
/// natural-key types the same collision was actively destructive: the row was deleted before the
/// colliding create, so the alarm vanished. See #462 and #463.
/// </remarks>
public sealed class TrackingConflictException : Exception
{
    public TrackingConflictException(string trackingType, string detail)
        : base(detail)
    {
        this.TrackingType = trackingType;
    }

    public TrackingConflictException()
    {
    }

    public TrackingConflictException(string message)
        : base(message)
    {
    }

    public TrackingConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public string? TrackingType
    {
        get;
    }
}
