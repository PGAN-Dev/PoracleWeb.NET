using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

/// <summary>
/// Pokestop-event alarms, over PoracleNG's v2 <c>incident</c> tracking type.
/// </summary>
/// <remarks>
/// <para>
/// One rule per event, enforced here rather than left to upstream. PoracleNG's create diffs on the
/// rule's identity — its <c>display_type</c> — so a create naming an event the user already tracks
/// silently rewrites that rule's radius and re-keys it, answering as though something new had been
/// made. Its replace does not diff at all: verified against 5.2.1, moving a Showcase rule onto
/// Kecleon while a Kecleon rule existed at a different radius left two Kecleon rules, a state no
/// other path can produce and the list has no way to describe. Both are refused before the write,
/// naming the rule in the way.
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

        var existing = await this._proxy.GetByUserAsync(userId);
        RefuseIfEventTaken(existing, model.DisplayType, ignoreUid: 0);

        var result = await this._proxy.CreateAsync(userId, [model]);
        model.Uid = result.PrimaryUid ?? 0;
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

        var existing = await this._proxy.GetByUserAsync(userId);
        foreach (var model in list)
        {
            model.Id = userId;
            RefuseIfEventTaken(existing, model.DisplayType, ignoreUid: 0);
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

        var result = await this._proxy.CreateAsync(userId, list);
        var written = result.Created.Concat(result.Updated).ToList();
        foreach (var model in list)
        {
            var match = written.FirstOrDefault(x => x.DisplayType == model.DisplayType);
            if (match is not null)
            {
                model.Uid = match.Uid;
            }
        }

        return list;
    }

    public async Task<PokestopEvent> UpdateAsync(string userId, PokestopEvent model)
    {
        await this._featureGate.EnsureEnabledAsync(DisableFeatureKeys.PokestopEvents);

        var existing = await this._proxy.GetByUserAsync(userId);
        RefuseIfEventTaken(existing, model.DisplayType, ignoreUid: model.Uid);

        var result = await this._proxy.ReplaceAsync(userId, model.Uid, model);

        // A replace is delete-then-insert upstream, so the surviving row is under a new uid.
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

    public async Task<int> UpdateDistanceByUserAsync(string userId, int profileNo, int distance)
    {
        var items = await this._proxy.GetByUserAsync(userId);
        return await this.RewriteDistanceAsync(userId, items, distance);
    }

    public async Task<int> UpdateDistanceByUidsAsync(List<int> uids, string userId, int distance)
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
    /// Re-sends the whole rule with a new radius rather than a patch, so template, clean bits and
    /// override scope survive.
    /// </summary>
    /// <remarks>
    /// Safe to send as one batch, unlike the v1 types: each rule's identity is its own event, so no
    /// two rows in the set can collapse into each other at the new radius — the #580/#598 failure has
    /// nothing to bite on here.
    /// </remarks>
    private async Task<int> RewriteDistanceAsync(string userId, IReadOnlyList<PokestopEvent> items, int distance)
    {
        if (items.Count == 0)
        {
            return 0;
        }

        foreach (var item in items)
        {
            item.Distance = distance;
        }

        await this._proxy.CreateAsync(userId, items);
        return items.Count;
    }

    private const string TrackingTypeName = "incident";

    /// <summary>Refuses a write that would land a second rule on an event the user already tracks.</summary>
    private static void RefuseIfEventTaken(IReadOnlyList<PokestopEvent> existing, int displayType, int ignoreUid)
    {
        if (existing.Any(x => x.DisplayType == displayType && x.Uid != ignoreUid))
        {
            throw new TrackingConflictException(
                TrackingTypeName,
                $"You already have an alarm for {NameOf(displayType)}. Edit that one instead.");
        }
    }

    /// <summary>
    /// The event's name for a message. Falls back to the raw id for an event this build has no name
    /// for — the message is worse, but refusing an id upstream accepts would be worse still.
    /// </summary>
    private static string NameOf(int displayType) =>
        PokestopEventTypes.NameFor(displayType) ?? $"display type {displayType}";
}
