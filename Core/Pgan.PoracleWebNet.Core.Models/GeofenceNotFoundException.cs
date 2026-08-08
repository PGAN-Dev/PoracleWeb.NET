namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>
/// The requested geofence does not exist.
/// </summary>
/// <remarks>
/// Distinct from the <see cref="InvalidOperationException"/> the geofence service throws for validation
/// and state-machine failures, so controllers can answer 404 for a missing record and 400 for bad input.
/// Previously both were the same type and every one of them became a 404 — so an admin who typed a
/// promoted name containing a slash was told the submission did not exist, while it sat visible in the
/// list in front of them. See #421.
/// </remarks>
/// <remarks>
/// Derives from <see cref="InvalidOperationException"/> on purpose: several controllers already catch
/// that type from these same service calls, and a sibling type would have turned every one of those
/// into an unhandled 500. Handlers that want to distinguish the two catch this first.
/// </remarks>
public sealed class GeofenceNotFoundException : InvalidOperationException
{
    public GeofenceNotFoundException(int id)
        : base($"Geofence with ID {id} not found.") => this.GeofenceId = id;

    public GeofenceNotFoundException(string kojiName)
        : base($"Geofence '{kojiName}' not found.") => this.KojiName = kojiName;

    public GeofenceNotFoundException()
    {
    }

    public GeofenceNotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public int? GeofenceId
    {
        get;
    }

    public string? KojiName
    {
        get;
    }
}
