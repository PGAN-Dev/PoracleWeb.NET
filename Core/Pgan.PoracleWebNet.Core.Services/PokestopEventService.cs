using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

/// <summary>
/// Pokestop-event alarms, over PoracleNG's v2 <c>incident</c> tracking type.
/// </summary>
/// <remarks>
/// <para>
/// Collisions are refused here rather than left to upstream. PoracleNG's create diffs the rule against
/// the stored ones like any invasion row, so a create one updatable field away from a rule the user
/// already has silently rewrites that rule and re-keys it, answering as though something new had been
/// made. Its replace does not diff at all, so an edit could land a rule that close beside another, a
/// state no create can produce. Both are refused before the write, naming the event. What is not
/// refused is a second rule for the same event that PoracleNG itself stores as one: limited to other
/// areas, measured from a place, or two updatable fields away (see <see cref="RefuseIfEventTaken"/>).
/// </para>
/// <para>
/// Every successful write re-keys the row: a create-as-update moved uid 732 to 733, a replace moved
/// 733 to 735. Callers must refetch rather than patch a list in place.
/// </para>
/// </remarks>
public class PokestopEventService(IPoracleIncidentProxy proxy, IFeatureGate featureGate, INaturalKeyCapabilityService naturalKeys) : IPokestopEventService
{
    private readonly IPoracleIncidentProxy _proxy = proxy;
    private readonly IFeatureGate _featureGate = featureGate;
    private readonly INaturalKeyCapabilityService _naturalKeys = naturalKeys;

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

        var keyEnforced = await this._naturalKeys.IsEnforcedAsync(TrackingTypeName);
        var existing = await this._proxy.GetByUserAsync(userId);
        RefuseIfEventTaken(existing, model, ignoreUid: 0, keyEnforced);

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

        var keyEnforced = await this._naturalKeys.IsEnforcedAsync(TrackingTypeName);
        var existing = await this._proxy.GetByUserAsync(userId);
        foreach (var model in list)
        {
            model.Id = userId;
            RefuseIfEventTaken(existing, model, ignoreUid: 0, keyEnforced);
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

        var keyEnforced = await this._naturalKeys.IsEnforcedAsync(TrackingTypeName);
        var existing = await this._proxy.GetByUserAsync(userId);
        RefuseIfEventTaken(existing, model, ignoreUid: model.Uid, keyEnforced);

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
    /// Re-sends the whole rule with a new radius rather than a patch, so template, clean bits and
    /// override scope survive.
    /// </summary>
    /// <remarks>
    /// Safe to send as one batch, unlike the v1 types: each rule's identity is its own event, so no
    /// two rows in the set can collapse into each other at the new radius — the #580/#598 failure has
    /// nothing to bite on here.
    /// </remarks>
    private async Task<DistanceUpdateResult> RewriteDistanceAsync(string userId, IReadOnlyList<PokestopEvent> items, int distance)
    {
        // A radius cannot apply to a rule limited to areas, and a rule measured from a place needs one.
        // PoracleNG refuses both, so one such rule in the batch failed all of them; skip and report it.
        var conflicts = items
            .Select(item => (item, conflict: DistanceRewrite.ConflictOf(
                item.OverrideAreas is { Count: > 0 },
                !string.IsNullOrWhiteSpace(item.OverrideLocationLabel),
                distance)))
            .ToList();
        var writable = conflicts.Where(c => c.conflict == DistanceRewrite.ScopeConflict.None).Select(c => c.item).ToList();
        var skipped = new DistanceUpdateResult(
            0,
            [.. conflicts.Where(c => c.conflict == DistanceRewrite.ScopeConflict.AreaScoped).Select(c => c.item.Uid)],
            [.. conflicts.Where(c => c.conflict == DistanceRewrite.ScopeConflict.PlaceScoped).Select(c => c.item.Uid)]);

        if (writable.Count == 0)
        {
            return skipped;
        }

        foreach (var item in writable)
        {
            item.Distance = distance;
        }

        await this._proxy.CreateAsync(userId, writable);
        return skipped with { Updated = writable.Count };
    }

    private const string TrackingTypeName = "incident";

    /// <summary>
    /// Refuses a write PoracleNG would resolve into a rule the user already has, or that its database
    /// would refuse outright.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Where the <c>invasion</c> table still carries its unique key (a server before migration 8), any second
    /// rule for the same event is a duplicate-key 500, so the event alone decides.
    /// </para>
    /// <para>
    /// Past migration 8 the rows are diffed like any invasion: <c>display_type</c> (stored as the event's
    /// name), the override areas and the place label identify a rule, and <c>distance</c>, <c>template</c> and
    /// <c>clean</c> are updatable. Verified by posting straight to <c>/api/v2/.../tracking/incident</c> on
    /// 5.2.1 and 5.3.0: beside an area-scoped twin, a different area list, a place label or two updatable
    /// differences it creates a second rule; an exact duplicate is <c>unchanged</c> and one updatable
    /// difference rewrites the stored rule. Only those last two are refused.
    /// </para>
    /// </remarks>
    private static void RefuseIfEventTaken(
        IReadOnlyList<PokestopEvent> existing, PokestopEvent submitted, int ignoreUid, bool keyEnforced)
    {
        // Any other rule within one updatable field refuses. On a create PoracleNG would find the first
        // such rule unchanged or take it over; on an edit the replace does not diff, and would leave two
        // rules that close together, a state no create can produce.
        var decisive = keyEnforced
            ? existing.FirstOrDefault(x => x.DisplayType == submitted.DisplayType && x.Uid != ignoreUid)
            : existing.FirstOrDefault(x => x.Uid != ignoreUid && UpdatableDifferences(x, submitted) is <= 1);

        if (decisive is not null)
        {
            throw new TrackingConflictException(
                TrackingTypeName,
                keyEnforced
                    ? $"You already have an alarm for {NameOf(submitted.DisplayType)}. Edit that one instead."
                    : $"You already have an alarm for {NameOf(submitted.DisplayType)} with those settings. Edit that one instead.");
        }
    }

    /// <summary>
    /// How many of PoracleNG's updatable fields differ, or null when an identity field does and the two
    /// are unrelated rules.
    /// </summary>
    private static int? UpdatableDifferences(PokestopEvent stored, PokestopEvent submitted)
    {
        if (stored.DisplayType != submitted.DisplayType
            || !AreasOf(stored).SequenceEqual(AreasOf(submitted), StringComparer.Ordinal)
            || !string.Equals(stored.OverrideLocationLabel ?? string.Empty, submitted.OverrideLocationLabel ?? string.Empty, StringComparison.Ordinal))
        {
            return null;
        }

        var differences = 0;
        if (stored.Distance != submitted.Distance)
        {
            differences++;
        }

        // v2 stores a blank template as "" and reads it back as null.
        if (!string.Equals(stored.Template ?? string.Empty, submitted.Template ?? string.Empty, StringComparison.Ordinal))
        {
            differences++;
        }

        // v2 carries only the three clean bits.
        if ((stored.Clean & 7) != (submitted.Clean & 7))
        {
            differences++;
        }

        return differences;
    }

    /// <summary>Area names as PoracleNG stores them: lowercased, in order, none meaning empty.</summary>
    private static IEnumerable<string> AreasOf(PokestopEvent rule) =>
        (rule.OverrideAreas ?? []).Select(a => a.ToLowerInvariant());

    /// <summary>
    /// The event's name for a message. Falls back to the raw id for an event this build has no name
    /// for — the message is worse, but refusing an id upstream accepts would be worse still.
    /// </summary>
    private static string NameOf(int displayType) =>
        PokestopEventTypes.NameFor(displayType) ?? $"display type {displayType}";
}
