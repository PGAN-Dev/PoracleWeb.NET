namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>
/// PoracleNG would not put a user's own fence on the active profile's area list.
/// </summary>
/// <remarks>
/// Derives from <see cref="InvalidOperationException"/> like <see cref="GeofenceNotFoundException"/>, so
/// handlers that catch the base type keep working; the activate endpoint catches this one first and
/// answers 409 rather than the 204 that would tell the user a switch-on happened.
/// </remarks>
public sealed class GeofenceActivationFailedException(string kojiName)
    : InvalidOperationException($"Geofence '{kojiName}' could not be switched on yet. Try again.")
{
    public string KojiName { get; } = kojiName;
}
