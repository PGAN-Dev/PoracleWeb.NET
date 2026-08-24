namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

/// <summary>
/// Whether the upstream PoracleNG can move a saved place without deleting it first.
/// </summary>
public interface IPlaceUpdateCapabilityService
{
    /// <summary>False on any server without <c>PUT /api/v2/humans/{id}/locations/{label}</c>.</summary>
    Task<bool> IsPlaceUpdateAvailableAsync(CancellationToken cancellationToken = default);
}
