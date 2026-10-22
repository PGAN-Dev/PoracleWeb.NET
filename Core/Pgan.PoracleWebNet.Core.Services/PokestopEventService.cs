using Microsoft.Extensions.Logging.Abstractions;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

/// <summary>
/// Pokestop-event alarms, over PoracleNG's v2 <c>incident</c> tracking type.
/// </summary>
/// <remarks>
/// <para>
/// PoracleNG's create diffs the rule against the stored ones like any invasion row: <c>display_type</c>,
/// the override areas and the place label identify a rule, and <c>distance</c>, <c>template</c> and
/// <c>clean</c> are updatable. So a create one updatable field away from a rule the user already has would
/// silently rewrite that rule and re-key it. Creates therefore go through the same dry-run protocol as every
/// other tracking type (<see cref="IncidentCreateProtocol"/>): PoracleNG says what the write would do, and a
/// takeover is refused before anything is written. A second rule for the same event that PoracleNG stores as
/// one (limited to other areas, measured from a place, two updatable fields away) is created as before. An
/// edit is one v2 replace, which PoracleNG refuses with a 409 when it would duplicate another rule.
/// </para>
/// <para>
/// Every successful write re-keys the row: a create-as-update moved uid 732 to 733, a replace moved
/// 733 to 735. Callers must refetch rather than patch a list in place.
/// </para>
/// </remarks>
public class PokestopEventService(IPoracleIncidentProxy proxy, IFeatureGate featureGate) : IPokestopEventService
{
    private readonly IPoracleIncidentProxy _proxy = proxy;
    private readonly IFeatureGate _featureGate = featureGate;

    public async Task<IEnumerable<PokestopEvent>> GetByUserAsync(string userId, int profileNo) =>
        await this._proxy.GetByUserAsync(userId);

    public async Task<PokestopEvent?> GetByUidAsync(string userId, int uid)
    {
        var items = await this._proxy.GetByUserAsync(userId);
        return items.FirstOrDefault(x => x.Uid == uid);
    }

    public async Task<PokestopEvent> CreateAsync(string userId, PokestopEvent model)
    {
        await this._featureGate.EnsureEnabledAsync(DisableFeatureKeys.PokestopEvents);
        model.Id = userId;

        var uids = await IncidentCreateProtocol.CreateAsync(this._proxy, userId, [model], single: true);
        model.Uid = uids[0];
        return model;
    }

    public async Task<IEnumerable<PokestopEvent>> BulkCreateAsync(string userId, IEnumerable<PokestopEvent> models)
    {
        await this._featureGate.EnsureEnabledAsync(DisableFeatureKeys.PokestopEvents);
        var list = models.ToList();
        if (list.Count == 0)
        {
            return list;
        }

        foreach (var model in list)
        {
            model.Id = userId;
        }

        // Two boxes ticked for the same event would collapse into one rule upstream and the caller
        // would be told both were made.
        var duplicate = list.GroupBy(x => x.DisplayType).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new TrackingConflictException(
                TrackingTypeName,
                $"'{NameOf(duplicate.Key)}' is named twice in the same request.");
        }

        var uids = await IncidentCreateProtocol.CreateAsync(this._proxy, userId, list, single: false);
        for (var i = 0; i < list.Count; i++)
        {
            list[i].Uid = uids[i];
        }

        return list;
    }

    public async Task<PokestopEvent> UpdateAsync(string userId, PokestopEvent model)
    {
        await this._featureGate.EnsureEnabledAsync(DisableFeatureKeys.PokestopEvents);

        var result = await this._proxy.ReplaceAsync(userId, model.Uid, model);

        // A replace is delete-then-insert upstream, so the surviving row is under a new uid (on 5.2.x; since
        // jfberry/PoracleNG #303 it keeps the old one unless the reassignment fails). Take whatever it says.
        model.Uid = result.PrimaryUid ?? model.Uid;
        return model;
    }

    public async Task<bool> DeleteAsync(string userId, int uid)
    {
        await this._proxy.DeleteByUidAsync(userId, uid);
        return true;
    }

    public async Task<int> DeleteAllByUserAsync(string userId, int profileNo)
    {
        var items = await this._proxy.GetByUserAsync(userId);
        if (items.Count == 0)
        {
            return 0;
        }

        return await this._proxy.BulkDeleteByUidsAsync(userId, items.Select(x => x.Uid));
    }

    public async Task<DistanceUpdateResult> UpdateDistanceByUserAsync(string userId, int profileNo, int distance)
    {
        var items = await this._proxy.GetByUserAsync(userId);
        return await this.RewriteDistanceAsync(userId, items, distance);
    }

    public async Task<DistanceUpdateResult> UpdateDistanceByUidsAsync(List<int> uids, string userId, int distance)
    {
        var selected = new HashSet<int>(uids);
        var items = await this._proxy.GetByUserAsync(userId);
        return await this.RewriteDistanceAsync(userId, [.. items.Where(x => selected.Contains(x.Uid))], distance);
    }

    public async Task<int> CountByUserAsync(string userId, int profileNo)
    {
        var items = await this._proxy.GetByUserAsync(userId);
        return items.Count;
    }

    /// <summary>
    /// One v2 PATCH naming the uids and the radius, so template, clean bits and override scope stay as stored
    /// and each rule keeps its uid.
    /// </summary>
    private async Task<DistanceUpdateResult> RewriteDistanceAsync(string userId, IReadOnlyList<PokestopEvent> items, int distance)
    {
        // A radius cannot apply to a rule limited to areas, and a rule measured from a place needs one.
        // PoracleNG refuses both; skip and report them rather than sending them.
        var plan = DistanceRewrite.Plan(
            items.Select(item => new DistanceRewrite.RowScope(
                item.Uid,
                item.OverrideAreas is { Count: > 0 },
                !string.IsNullOrWhiteSpace(item.OverrideLocationLabel))),
            distance);

        if (plan.Writable.Count == 0)
        {
            return DistanceRewrite.Report(plan, TrackingTypeName, [], NullLogger.Instance);
        }

        var result = await this._proxy.PatchAsync(userId, plan.WritableUids, new TrackingPatchSet(Distance: distance));
        return DistanceRewrite.Report(plan, TrackingTypeName, [result], NullLogger.Instance);
    }

    private const string TrackingTypeName = "incident";

    /// <summary>
    /// The event's name for a message. Falls back to the raw id for an event this build has no name
    /// for — the message is worse, but refusing an id upstream accepts would be worse still.
    /// </summary>
    private static string NameOf(int displayType) =>
        PokestopEventTypes.NameFor(displayType) ?? $"display type {displayType}";
}
