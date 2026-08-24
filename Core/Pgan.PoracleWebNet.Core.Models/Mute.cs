namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>
/// One active quiet period on a PoracleNG human: alerts matching <see cref="Scope"/> and
/// <see cref="Value"/> are suppressed until <see cref="ExpiresAt"/>.
/// </summary>
/// <remarks>
/// A mute has no id upstream -- its identity is (scope, value) per human, which is why deletes address
/// it by those two fields rather than a uid. It is held in PoracleNG's memory and is cleared by a
/// processor restart, so nothing here should be cached across a page's lifetime without refetching.
/// </remarks>
public sealed class Mute
{
    /// <summary>One of <see cref="MuteScopes"/>.</summary>
    public string Scope { get; init; } = string.Empty;

    /// <summary>
    /// Scope identifier as the SERVER canonicalised it -- null only for <c>everything</c>. Area names come
    /// back in the geofence's own casing ("aberdeen" is stored as "Aberdeen") and DELETE matches the
    /// string exactly, so a resume must send this value back rather than whatever was submitted.
    /// </summary>
    public string? Value
    {
        get; init;
    }

    /// <summary>Unix seconds at which the mute lapses.</summary>
    public long ExpiresAt
    {
        get; init;
    }

    /// <summary>Seconds until expiry, as computed by the server when it answered.</summary>
    public long RemainingSecs
    {
        get; init;
    }
}
