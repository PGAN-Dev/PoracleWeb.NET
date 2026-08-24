using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

/// <summary>
/// Whether the PoracleNG this instance talks to has the costume columns at all.
/// </summary>
/// <remarks>
/// Costume is the quietest kind of unsupported: a server without the migration accepts the extra key,
/// answers 200 and drops it, so a filter that reads "Halloween 2025" on the card matches every Pikachu
/// in the area. Nothing in the response says so — this is the only thing that can.
/// </remarks>
public interface ICostumeCapabilityService
{
    /// <summary>
    /// What this server can store, per alarm type. Never throws: an unreachable server, an unread
    /// migration number or a fault while probing all come back as
    /// <see cref="CostumeCapability.None"/>, because every caller is asking "may I offer this", and the
    /// answer when nobody knows is no.
    /// </summary>
    Task<CostumeCapability> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Refuses a costume this server cannot store, before it is written.
    /// </summary>
    /// <remarks>
    /// The wildcard <see cref="CostumeCapability.AnyCostume"/> always passes: it asks for no filtering
    /// and is what PoracleNG stores for an absent key anyway, so a rule carrying it is unaffected by the
    /// column's absence. Anything narrower on a server without the column would save, report success and
    /// filter nothing, so it is refused rather than silently widened — a rule that quietly stopped
    /// meaning what it says is worse than one that would not save.
    /// </remarks>
    /// <param name="trackingType">PoracleNG's tracking type, <c>pokemon</c> or <c>raid</c>.</param>
    /// <param name="costume">The costume the alarm is about to be written with.</param>
    /// <exception cref="AlarmValidationException">The server cannot store this costume.</exception>
    Task EnsureCostumeWritableAsync(string trackingType, int costume, CancellationToken cancellationToken = default);
}
