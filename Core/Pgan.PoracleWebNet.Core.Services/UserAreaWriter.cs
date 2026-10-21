using System.Text.Json;

using Microsoft.Extensions.Logging;
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
public partial class UserAreaWriter(
    IPoracleHumanProxy humanProxy,
    IUserGeofenceRepository geofences,
    IPoracleTrackingProxy trackingProxy,
    ILogger<UserAreaWriter> logger) : IUserAreaWriter
{
    private readonly IPoracleHumanProxy _humanProxy = humanProxy;
    private readonly IUserGeofenceRepository _geofences = geofences;
    private readonly IPoracleTrackingProxy _trackingProxy = trackingProxy;
    private readonly ILogger<UserAreaWriter> _logger = logger;

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

    public async Task<AreaDeleteImpact> PreviewRemovalAsync(string humanId, string name)
    {
        var preview = await this._humanProxy.RemoveAreaEverywhereAsync(humanId, Clean(name), dryRun: true);
        var counts = Widened(preview)
            .ToDictionary(w => w.Key, w => w.Value.Count, StringComparer.Ordinal);
        return new AreaDeleteImpact(counts, counts.Values.Sum());
    }

    /// <remarks>
    /// <para>
    /// The widened rules are deleted before the name is removed, so a failure part-way leaves the fence and
    /// its alarms in place for a retry rather than a set of alarms that already match everywhere.
    /// </para>
    /// <para>
    /// PoracleNG's bulk delete is per profile and skips a uid that is not on the profile it names (verified
    /// on the 5.4.0 bed; <c>all_profiles</c> is a 422 there), and the dry run does not say which profile a
    /// uid is on. So each type's uids go to each profile in turn, carrying only those not yet deleted. The
    /// profiles are the ones PoracleNG's removal targets: every profile row, or the active profile when the
    /// human has none.
    /// </para>
    /// </remarks>
    public async Task RemoveEverywhereAsync(string humanId, string name, bool deleteWidenedAlarms)
    {
        var clean = Clean(name);

        if (deleteWidenedAlarms)
        {
            var preview = await this._humanProxy.RemoveAreaEverywhereAsync(humanId, clean, dryRun: true);
            var widened = Widened(preview);
            if (widened.Count > 0)
            {
                var profiles = await this.TargetProfilesAsync(humanId, preview.RemovedFrom);
                foreach (var (type, uids) in widened)
                {
                    var remaining = await this.DeleteOnEachProfileAsync(type, humanId, uids, profiles);

                    // PoracleNG reports Pokestop event rules under "invasion" (they share the table), but the
                    // v2 invasion delete filters event rows out and skips them. What the invasion pass left
                    // is tried again as "incident", or every event alarm would survive and then widen.
                    if (type == InvasionType && remaining.Count > 0)
                    {
                        remaining = await this.DeleteOnEachProfileAsync(IncidentType, humanId, remaining, profiles);
                    }

                    if (remaining.Count > 0)
                    {
                        LogWidenedAlarmsNotDeleted(this._logger, humanId, clean, type, string.Join(", ", remaining));
                    }
                }
            }
        }

        _ = await this._humanProxy.RemoveAreaEverywhereAsync(humanId, clean, dryRun: false);
    }

    private const string InvasionType = "invasion";
    private const string IncidentType = "incident";

    /// <returns>The uids no profile's delete removed.</returns>
    private async Task<List<int>> DeleteOnEachProfileAsync(string type, string humanId, List<int> uids, List<int> profiles)
    {
        var remaining = uids;
        foreach (var profileNo in profiles)
        {
            if (remaining.Count == 0)
            {
                break;
            }

            var deleted = await this._trackingProxy.BulkDeleteByUidsOnProfileAsync(type, humanId, remaining, profileNo);
            remaining = remaining.Except(deleted).ToList();
        }

        return remaining;
    }

    private static Dictionary<string, List<int>> Widened(AreaRemovalResult result) =>
        result.Broadened
            .Select(b => (b.Key, Uids: b.Value.Distinct().ToList()))
            .Where(b => b.Uids.Count > 0)
            .ToDictionary(b => b.Key, b => b.Uids, StringComparer.Ordinal);

    private async Task<List<int>> TargetProfilesAsync(string humanId, IReadOnlyList<int> removedFrom)
    {
        var profiles = new SortedSet<int>(removedFrom);

        var json = await this._humanProxy.GetProfilesAsync(humanId);
        if (json.ValueKind == JsonValueKind.Object
            && json.TryGetProperty("profile", out var list)
            && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var profile in list.EnumerateArray())
            {
                if (profile.ValueKind == JsonValueKind.Object
                    && profile.TryGetProperty("profile_no", out var no)
                    && no.TryGetInt32(out var number))
                {
                    _ = profiles.Add(number);
                }
            }
        }

        if (profiles.Count == 0
            && await this._humanProxy.GetHumanAsync(humanId) is { } human
            && human.TryGetProperty("current_profile_no", out var current)
            && current.TryGetInt32(out var active))
        {
            _ = profiles.Add(active);
        }

        return [.. profiles];
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Removing area '{Name}' for {HumanId}: {Type} alarms {Uids} were limited to it but no profile delete removed them; they now match across the profile's areas")]
    private static partial void LogWidenedAlarmsNotDeleted(ILogger logger, string humanId, string name, string type, string uids);

    private static string Clean(string name) => name.Trim().ToLowerInvariant();

    private static string Key(string name) => Clean(name).Replace('_', ' ');
}
