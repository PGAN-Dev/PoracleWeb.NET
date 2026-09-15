namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>
/// The rule the write addressed does not exist, or does not belong to this user.
/// </summary>
/// <remarks>
/// Only the uid-addressed <c>/api/v2</c> PUT can tell us this: it is scoped by (human, uid) and answers
/// 404 rather than quietly creating a row. The v1 surface takes a submitted uid at face value, which is
/// the whole reason a stale uid used to strand an alarm on a profile nobody could see (#411). Reporting
/// it as 404 rather than a 500 lets the SPA reload the list instead of telling the user the server broke.
/// </remarks>
public sealed class TrackingRuleNotFoundException : Exception
{
    public TrackingRuleNotFoundException(string trackingType, string detail)
        : base(detail)
    {
        this.TrackingType = trackingType;
    }

    public TrackingRuleNotFoundException()
    {
    }

    public TrackingRuleNotFoundException(string message)
        : base(message)
    {
    }

    public TrackingRuleNotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public string? TrackingType
    {
        get;
    }
}
