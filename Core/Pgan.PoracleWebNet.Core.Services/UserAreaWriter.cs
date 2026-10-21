using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

/// <summary>
/// The one writer of a user's area list. A trusted PoracleNG write skips its own allowed-areas filter, so
/// this class authorises the names first.
/// </summary>
/// <remarks>
/// PoracleNG still drops a name it has no fence for, trusted or not, and reports it under <c>rejected</c>.
/// Production holds names like that (renamed fences, &amp;-escaped names), so a rejected name is data the
/// caller gets back as <see cref="AreaSaveResult.Dropped"/>, never an error. Names are compared lowercase and
/// trimmed with <c>_</c> and space equal, which is how PoracleNG keys them (NormalizeAreaKey).
/// </remarks>
public class UserAreaWriter(IPoracleHumanProxy humanProxy, IUserGeofenceRepository geofences) : IUserAreaWriter
{
    private readonly IPoracleHumanProxy _humanProxy = humanProxy;
    private readonly IUserGeofenceRepository _geofences = geofences;

    public async Task<AreaSaveResult> SaveSelectionAsync(string humanId, IReadOnlyCollection<string> requested)
    {
        var names = requested
            .Select(Clean)
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var held = await this._humanProxy.GetAreaListAsync(humanId);
        var available = await this._humanProxy.GetAvailableAreasAsync(humanId);
        var own = await this._geofences.GetByHumanIdAsync(humanId);

        var allowed = new HashSet<string>(StringComparer.Ordinal);
        allowed.UnionWith(available.Where(a => a.UserSelectable).Select(a => Key(a.Name)));
        allowed.UnionWith(own.Select(g => Key(g.KojiName)));
        allowed.UnionWith(held.Select(Key));

        var refused = names.Where(n => !allowed.Contains(Key(n))).ToList();
        if (refused.Count > 0)
        {
            throw new AreaNotPermittedException(refused);
        }

        var rejected = await this._humanProxy.SetAreasTrustedAsync(humanId, names);
        var dropped = rejected.Select(Clean).ToList();
        var droppedKeys = dropped.Select(Key).ToHashSet(StringComparer.Ordinal);
        return new AreaSaveResult(names.Where(n => !droppedKeys.Contains(Key(n))).ToList(), dropped);
    }

    public async Task<bool> AddToActiveProfileAsync(string humanId, string name)
    {
        var clean = Clean(name);
        var current = await this._humanProxy.GetAreaListAsync(humanId);
        if (clean.Length == 0 || current.Any(c => Key(c) == Key(clean)))
        {
            return false;
        }

        var rejected = await this._humanProxy.SetAreasTrustedAsync(humanId, [.. current, clean]);
        return !rejected.Any(r => Key(r) == Key(clean));
    }

    public async Task<bool> RemoveFromActiveProfileAsync(string humanId, string name)
    {
        var key = Key(name);
        var current = await this._humanProxy.GetAreaListAsync(humanId);
        var kept = current.Where(c => Key(c) != key).ToList();
        if (kept.Count == current.Count)
        {
            return false;
        }

        _ = await this._humanProxy.SetAreasTrustedAsync(humanId, kept);
        return true;
    }

    private static string Clean(string name) => name.Trim().ToLowerInvariant();

    private static string Key(string name) => Clean(name).Replace('_', ' ');
}
