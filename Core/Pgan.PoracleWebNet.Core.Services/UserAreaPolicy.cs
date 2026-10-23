using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

/// <inheritdoc />
/// <remarks>
/// Depends on the human proxy and the geofence repository only. <see cref="UserAreaWriter"/> needs the
/// tracking proxy and the tracking proxy needs this policy, so a dependency on it here would be a cycle.
/// </remarks>
public class UserAreaPolicy(IPoracleHumanProxy humanProxy, IUserGeofenceRepository geofences) : IUserAreaPolicy
{
    private readonly IPoracleHumanProxy _humanProxy = humanProxy;
    private readonly IUserGeofenceRepository _geofences = geofences;

    public async Task<IReadOnlySet<string>> AllowedKeysAsync(string humanId)
    {
        var held = await this._humanProxy.GetAreaListAsync(humanId);
        var available = await this._humanProxy.GetAvailableAreasAsync(humanId);
        var own = await this._geofences.GetByHumanIdAsync(humanId);

        var allowed = new HashSet<string>(StringComparer.Ordinal);
        allowed.UnionWith(available.Where(a => a.UserSelectable).Select(a => Key(a.Name)));
        allowed.UnionWith(own.Select(g => Key(g.KojiName)));
        allowed.UnionWith(held.Select(Key));
        return allowed;
    }

    public async Task EnsureOverrideAreasAllowedAsync(
        string humanId,
        IReadOnlyCollection<string> requested,
        Func<Task<IReadOnlyCollection<string>>>? storedOnRule)
    {
        var names = requested
            .Select(Clean)
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (names.Count == 0)
        {
            return;
        }

        var allowed = await this.AllowedKeysAsync(humanId);
        var refused = names.Where(n => !allowed.Contains(Key(n))).ToList();
        if (refused.Count > 0 && storedOnRule is not null)
        {
            var stored = (await storedOnRule()).Select(Key).ToHashSet(StringComparer.Ordinal);
            refused = refused.Where(n => !stored.Contains(Key(n))).ToList();
        }

        if (refused.Count > 0)
        {
            throw new AlarmValidationException($"These areas can't be used on an alarm: {string.Join(", ", refused)}.");
        }
    }

    public static string Clean(string name) => name.Trim().ToLowerInvariant();

    public static string Key(string name) => Clean(name).Replace('_', ' ');
}
