using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

/// <inheritdoc />
/// <remarks>
/// Depends on the human proxy and the geofence repository only. <see cref="UserAreaWriter"/> needs the
/// tracking proxy and the tracking proxy needs this policy, so a dependency on it here would be a cycle.
/// </remarks>
public partial class UserAreaPolicy(
    IPoracleHumanProxy humanProxy,
    IUserGeofenceRepository geofences,
    Lazy<ILinkedAccount>? linkedAccount = null,
    IPoracleApiProxy? poracleApiProxy = null,
    ILogger<UserAreaPolicy>? logger = null) : IUserAreaPolicy
{
    private readonly IPoracleHumanProxy _humanProxy = humanProxy;
    private readonly IUserGeofenceRepository _geofences = geofences;
    private readonly Lazy<ILinkedAccount>? _linkedAccount = linkedAccount;
    private readonly IPoracleApiProxy? _poracleApiProxy = poracleApiProxy;
    private readonly ILogger _logger = (ILogger?)logger ?? NullLogger.Instance;

    // Read once per request: a profile import checks every rule it creates.
    private Task<PoracleConfig?>? _config;

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
    /// The same answer <see cref="ILinkedAccount"/> gives everywhere else (linking switched off, no link, and a
    /// side with no human all included). It is resolved lazily: <c>ILinkedAccount</c> reads humans, and the
    /// tracking proxy that depends on this policy sits behind that, so a constructor dependency would be a cycle.
    /// </summary>
    private async Task<IReadOnlyList<string>> OwnerIdsAsync(string humanId)
    {
        if (this._linkedAccount is null)
        {
            return [humanId];
        }

        var pair = await this._linkedAccount.Value.ResolveAsync(humanId);
        return pair.ExistingHumanIds.Contains(humanId, StringComparer.Ordinal) ? pair.ExistingHumanIds : [humanId, .. pair.ExistingHumanIds];
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

    public Task EnsureAreaListWithinLimitAsync(
        string humanId,
        IReadOnlyCollection<string> areas,
        Func<Task<IReadOnlyCollection<string>>>? previous) =>
        this.EnsureWithinLimitAsync(humanId, areas, previous, max => $"You can select at most {max} areas. Remove one before adding another.");

    public Task EnsureOverrideAreasWithinLimitAsync(
        string humanId,
        IReadOnlyCollection<string> areas,
        Func<Task<IReadOnlyCollection<string>>>? storedOnRule) =>
        this.EnsureWithinLimitAsync(humanId, areas, storedOnRule, max => $"An alarm can be limited to at most {max} areas.");

    /// <summary>
    /// PoracleNG's own <c>AreaLimitBlocks</c> (jfberry/PoracleNG#346), which it skips for the trusted writes this
    /// application sends: over the limit is refused unless every name is already in the list being replaced, or
    /// the human is exempt. Duplicates count once. An unreadable config is no limit, as PoracleNG's own fails open.
    /// </summary>
    private async Task EnsureWithinLimitAsync(
        string humanId,
        IReadOnlyCollection<string> areas,
        Func<Task<IReadOnlyCollection<string>>>? previous,
        Func<int, string> message)
    {
        var keys = areas.Select(Key).Where(k => k.Length > 0).ToHashSet(StringComparer.Ordinal);
        if (keys.Count == 0 || this._poracleApiProxy is null)
        {
            return;
        }

        PoracleConfig? config;
        try
        {
            config = await (this._config ??= this._poracleApiProxy.GetConfigAsync());
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            this._config = null;
            LogAreaLimitUnreadable(this._logger, ex);
            return;
        }

        var max = config?.MaxAreas ?? 0;
        if (max <= 0 || keys.Count <= max)
        {
            return;
        }

        if (previous is not null && keys.IsSubsetOf((await previous()).Select(Key)))
        {
            return;
        }

        if (await this.IsLimitExemptAsync(humanId, config!))
        {
            return;
        }

        throw new AlarmValidationException(message(max));
    }

    /// <summary>
    /// PoracleNG's <c>LimitExempt</c>: its admins, and every destination that isn't a person (channels, webhooks,
    /// groups, threads), which only admins and delegated admins configure. Judged on the human written to, as
    /// PoracleNG does, so an admin managing someone else's account is held to that person's limit.
    /// </summary>
    private async Task<bool> IsLimitExemptAsync(string humanId, PoracleConfig config)
    {
        if (config.Admins is { } admins
            && (admins.Discord.Contains(humanId, StringComparer.Ordinal) || admins.Telegram.Contains(humanId, StringComparer.Ordinal)))
        {
            return true;
        }

        var human = await this._humanProxy.GetHumanAsync(humanId);
        return human is { ValueKind: JsonValueKind.Object } h
            && h.TryGetProperty("type", out var type)
            && type.ValueKind == JsonValueKind.String
            && !(type.GetString() ?? string.Empty).EndsWith(":user", StringComparison.Ordinal);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not read PoracleNG's max_areas; the area limit is not checked")]
    private static partial void LogAreaLimitUnreadable(ILogger logger, Exception ex);

    public static string Clean(string name) => name.Trim().ToLowerInvariant();

    public static string Key(string name) => Clean(name).Replace('_', ' ');
}
