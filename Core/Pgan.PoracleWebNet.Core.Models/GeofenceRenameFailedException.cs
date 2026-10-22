namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>
/// PoracleNG would not take a fence's new name, so the rename was rolled back and nothing changed.
/// </summary>
/// <remarks>
/// Derives from <see cref="InvalidOperationException"/> like <see cref="GeofenceActivationFailedException"/>;
/// the rename endpoint catches this one first and answers 409, because the user's input was fine and a
/// retry can succeed.
/// </remarks>
public sealed class GeofenceRenameFailedException()
    : InvalidOperationException("The geofence could not be renamed right now, so nothing was changed. Try again in a moment.");
