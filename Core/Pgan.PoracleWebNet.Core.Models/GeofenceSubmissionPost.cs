namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>
/// Everything the Discord review post shows about a submitted geofence. Built once at submission time and
/// rebuilt on approval/rejection so the opening post can be edited in place to reflect the outcome.
/// </summary>
public sealed record GeofenceSubmissionPost
{
    /// <summary>Discord user ID of the submitter, used for the clickable mention.</summary>
    public required string UserId { get; init; }

    /// <summary>
    /// Display name of the submitter, shown in the embed author block. Null when it could not be resolved,
    /// in which case the author block is omitted rather than showing a raw Discord ID.
    /// </summary>
    public string? UserName { get; init; }

    /// <summary>Name the submitter gave the area.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Lowercase name the area would take in the shared public list on approval.</summary>
    public required string PublicName { get; init; }

    /// <summary>Auto-detected Koji parent region, or empty when detection found nothing.</summary>
    public string GroupName { get; init; } = string.Empty;

    public double AreaSqKm { get; init; }

    public double CentroidLat { get; init; }

    public double CentroidLon { get; init; }

    /// <summary>Name of an existing public area containing this one's centroid, when there is one.</summary>
    public string? OverlapsArea { get; init; }

    /// <summary>Static map URL from Poracle; downloaded and attached to the message.</summary>
    public string? MapImageUrl { get; init; }

    /// <summary>Review state driving the embed colour and status line: pending, approved or rejected.</summary>
    public GeofenceReviewState State { get; init; } = GeofenceReviewState.Pending;

    /// <summary>Admin's reason, shown on rejection.</summary>
    public string? ReviewNotes { get; init; }

    /// <summary>Deep link to the admin review page, when a public site URL is configured.</summary>
    public string? ReviewUrl { get; init; }
}

public enum GeofenceReviewState
{
    Pending,
    Approved,
    Rejected,
}
