using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

/// <summary>
/// Source of the 19 (currently) known Pokémon GO raid levels, sourced from the
/// WatWowMap masterfile. Served as a structured list to the frontend so the
/// level selector and alarm cards stay aligned with the canonical vocabulary
/// even as new raid types ship.
///
/// Implementations should cache the result and fall back to a baked-in list
/// when the upstream masterfile is unreachable.
/// </summary>
public interface IRaidLevelService
{
    /// <summary>Returns the canonical raid-level list. Never throws; falls back to defaults on error.</summary>
    Task<IReadOnlyList<RaidLevelInfo>> GetAllAsync();
}
