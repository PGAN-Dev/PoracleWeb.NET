using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

/// <inheritdoc />
/// <remarks>
/// Depends on the human proxy and the geofence repository only. <see cref="UserAreaWriter"/> needs the
/// tracking proxy and the tracking proxy needs this policy, so a dependency on it here would be a cycle.
/// </remarks>
public class UserAreaPolicy(
    IPoracleHumanProxy humanProxy,
    IUserGeofenceRepository geofences,
    IAccountLinkRepository? links = null,
    IFeatureGate? featureGate = null) : IUserAreaPolicy
{
    private readonly IPoracleHumanProxy _humanProxy = humanProxy;
    private readonly IUserGeofenceRepository _geofences = geofences;
    private readonly IAccountLinkRepository? _links = links;
    private readonly IFeatureGate? _featureGate = featureGate;

    public async Task<IReadOnlySet<string>> AllowedKeysAsync(string humanId)
    {
        var held = await this._humanProxy.GetAreaListAsync(humanId);
        var available = await this._humanProxy.GetAvailableAreasAsync(humanId);
        var own = new List<UserGeofence>();
        foreach (var id in await this.OwnerIdsAsync(humanId))
        {
            own.AddRange(await this._geofences.GetByHumanIdAsync(id));
        }

        var allowed = new HashSet<string>(StringComparer.Ordinal);
        allowed.UnionWith(available.Where(a => a.UserSelectable).Select(a => Key(a.Name)));
        allowed.UnionWith(own.Select(g => Key(g.KojiName)));
        allowed.UnionWith(held.Select(Key));
        return allowed;
    }

    /// <summary>
    /// The ids whose drawn fences count as this human's own: itself and, for a linked pair, the other id.
    /// Read from the link table directly rather than through <c>ILinkedAccount</c>, which reads humans, and the
    /// tracking proxy that depends on this policy sits behind that.
    /// </summary>
    private async Task<IReadOnlyList<string>> OwnerIdsAsync(string humanId)
    {
        if (this._links is null
            || (this._featureGate is not null && !await this._featureGate.IsEnabledAsync(DisableFeatureKeys.AccountLinking)))
        {
            return [humanId];
        }

        var link = await this._links.GetByHumanIdAsync(humanId);
        return link is null ? [humanId] : [humanId, string.Equals(link.DiscordId, humanId, StringComparison.Ordinal) ? link.TelegramId : link.DiscordId];
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
