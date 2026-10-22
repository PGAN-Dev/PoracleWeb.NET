using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

/// <summary>
/// The create protocol for Pokestop-event rules: a dry run of every rule, then a commit of only the rules the
/// dry run reports as new. Used by <see cref="PokestopEventService"/> and by <see cref="InvasionService"/> for
/// the event grunts the invasion dialog offers where there is no Pokestop Events page.
/// </summary>
/// <remarks>
/// <para>
/// A typed copy of the tracking proxy's protocol (<c>PoracleTrackingProxy.CreateCoreAsync</c>), with the same
/// classification and the same message: identical is <c>unchanged</c>, one updatable field away is
/// <c>updated</c> (the stored rule would take the new value, a takeover), anything else is <c>created</c>. A
/// single rule that would update is refused; one that is unchanged is reported as already present with uid 0.
/// A batch writes the rules that are new and leaves the rest at uid 0. <c>TrackingCreateProtocolTests</c> pins
/// the tracking side, <c>PokestopEventServiceTests</c> this one; a change to one belongs in both.
/// </para>
/// <para>
/// The answer's buckets carry no index back to the request, so a mixed batch asks about each rule alone.
/// PoracleNG classifies against stored rules only, never between the rules of one batch. A commit that finds
/// a takeover the dry run did not (another write landed in between) puts the rule back from the
/// <c>previous</c> state PoracleNG reports.
/// </para>
/// </remarks>
internal static class IncidentCreateProtocol
{
    /// <summary>Creates <paramref name="rules"/>.</summary>
    /// <param name="single">One rule the user asked for: a takeover is refused rather than skipped.</param>
    /// <param name="profileNo">The profile to write to (<c>?profile=N</c>); null for the active one.</param>
    /// <returns>The uid each rule was created under, index for index; 0 where nothing was written.</returns>
    public static async Task<int[]> CreateAsync(
        IPoracleIncidentProxy proxy, string userId, IReadOnlyList<PokestopEvent> rules, bool single, int? profileNo = null)
    {
        Task<PokestopEventWriteResult> Post(IEnumerable<PokestopEvent> batch, bool dryRun) =>
            profileNo is { } no ? proxy.CreateAsync(userId, batch, no, dryRun) : proxy.CreateAsync(userId, batch, dryRun);

        Task<PokestopEventWriteResult> Put(int uid, PokestopEvent rule) =>
            profileNo is { } no ? proxy.ReplaceAsync(userId, uid, rule, no) : proxy.ReplaceAsync(userId, uid, rule);

        var uids = new int[rules.Count];
        if (rules.Count == 0)
        {
            return uids;
        }

        var dry = await Post(rules, dryRun: true);
        List<int> keep;

        if (dry.Updated.Count == 0 && dry.Unchanged.Count == 0)
        {
            keep = [.. Enumerable.Range(0, rules.Count)];
        }
        else if (single)
        {
            if (dry.Updated.Count > 0)
            {
                throw new TrackingConflictException(TrackingType, PoracleTrackingProxy.TakeoverMessage);
            }

            // An identical rule is already stored; nothing is written.
            return uids;
        }
        else
        {
            keep = [];
            for (var i = 0; i < rules.Count; i++)
            {
                // One rule's batch answer already is its own classification.
                var verdict = rules.Count == 1
                    ? VerdictOf(dry)
                    : VerdictOf(await Post([rules[i]], dryRun: true));

                if (verdict == PoracleTrackingProxy.DryRunVerdict.Created)
                {
                    keep.Add(i);
                }
            }

            if (keep.Count == 0)
            {
                return uids;
            }
        }

        var commit = await Post([.. keep.Select(i => rules[i])], dryRun: false);

        for (var u = 0; u < commit.Updated.Count; u++)
        {
            var previous = commit.Previous is { } all && u < all.Count ? all[u] : null;
            if (previous is not null && previous.Uid > 0)
            {
                await Put(previous.Uid, previous);
            }
        }

        if (single && commit.Updated.Count > 0)
        {
            throw new TrackingConflictException(TrackingType, PoracleTrackingProxy.TakeoverMessage);
        }

        // Paired on the event rather than on position: PoracleNG reorders its answer. Each created rule is
        // used once, so a request naming one event twice (for two scopes) still gets two uids.
        var created = commit.Created.ToList();
        foreach (var i in keep)
        {
            var match = created.FirstOrDefault(c => c.DisplayType == rules[i].DisplayType);
            if (match is not null)
            {
                uids[i] = match.Uid;
                created.Remove(match);
            }
        }

        return uids;
    }

    /// <summary>
    /// What a dry run of ONE rule says about it, by the tracking side's rule: a takeover outranks everything,
    /// then an exact duplicate; an answer that says nothing is left for the commit.
    /// </summary>
    internal static PoracleTrackingProxy.DryRunVerdict VerdictOf(PokestopEventWriteResult result) =>
        result.Updated.Count > 0 ? PoracleTrackingProxy.DryRunVerdict.Updated
        : result.Unchanged.Count > 0 && result.Created.Count == 0 ? PoracleTrackingProxy.DryRunVerdict.Unchanged
        : PoracleTrackingProxy.DryRunVerdict.Created;

    private const string TrackingType = "incident";
}
