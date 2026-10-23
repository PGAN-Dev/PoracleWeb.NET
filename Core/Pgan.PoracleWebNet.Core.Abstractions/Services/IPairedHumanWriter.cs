using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

/// <summary>
/// Carries a person-level write (location, places, areas, profiles, language, quiet periods, alerts on or off,
/// summary schedules) to both humans of a linked pair. With no link it is one call, today's.
/// </summary>
public interface IPairedHumanWriter
{
    /// <summary>
    /// Runs <paramref name="write"/> for the signed-in human, then for the other existing human. The first
    /// throwing propagates and the second is not tried. The second throwing is logged and returned as
    /// <see cref="PairWriteResult{T}.Warning"/>; nothing is rolled back.
    /// </summary>
    public Task<PairWriteResult<T>> WriteAsync<T>(string userId, Func<string, Task<T>> write);

    public Task<PairWriteResult<bool>> WriteAsync(string userId, Func<string, Task> write);

    /// <summary>Which side of the signed-in person's link a human id is, or null when unlinked or not in it.</summary>
    public Task<LinkSide?> SideOfAsync(string userId, string humanId);
}
