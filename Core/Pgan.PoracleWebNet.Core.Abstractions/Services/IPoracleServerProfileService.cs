using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

/// <summary>
/// Reads which PoracleNG this instance is talking to, and what it can store.
/// </summary>
public interface IPoracleServerProfileService
{
    /// <summary>
    /// The current profile, cached briefly. Never throws: a server that cannot be reached comes back as
    /// <see cref="PoracleServerProfile.Unknown"/> rather than an exception, because every caller is
    /// asking "may I offer this feature", and the answer when nobody knows is no.
    /// </summary>
    Task<PoracleServerProfile> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>Drops the cached profile so the next read probes again.</summary>
    void Invalidate();
}
