using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

/// <summary>
/// Reads what the upstream PoracleNG's <c>/api/v2</c> surface carries, from the OpenAPI document it
/// publishes about itself.
/// </summary>
public interface IPoracleV2SchemaService
{
    /// <summary>
    /// The current capabilities, cached briefly. Never throws: a server that cannot be reached, or a
    /// document that will not parse, comes back as <see cref="PoracleV2Capabilities.None"/>, because
    /// every caller is asking "may I use the new path", and the answer when nobody knows is no.
    /// </summary>
    Task<PoracleV2Capabilities> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>Drops the cached answer so the next read probes again.</summary>
    void Invalidate();
}
