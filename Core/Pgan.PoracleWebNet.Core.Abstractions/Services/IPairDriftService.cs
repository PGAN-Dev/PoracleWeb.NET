using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

/// <summary>Finds where a linked pair's humans have come apart and, on the person's choice, makes the other side match.</summary>
public interface IPairDriftService
{
    /// <summary>Empty for an unlinked pair (no PoracleNG call) or when either side has no human.</summary>
    public Task<IReadOnlyList<DriftItem>> GetAsync(LinkedPair pair);

    /// <summary>The side that has a legacy profile 0 the other lacks; null when neither or both do, or for an unlinked pair (no PoracleNG call).</summary>
    public Task<LinkSide?> GetLegacyProfileZeroAsync(LinkedPair pair);

    /// <exception cref="DriftChangedException">The re-read differs from <paramref name="expected"/>.</exception>
    /// <exception cref="DriftProfileHoldsAlarmsException">A profile to delete is not empty.</exception>
    public Task ResolveAsync(LinkedPair pair, DriftField field, LinkSide keep, string expected);
}
