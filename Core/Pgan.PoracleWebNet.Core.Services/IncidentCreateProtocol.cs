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
/// The answer's buckets carry no index back to the request, so a mixed batch is told apart by
/// <see cref="BatchDryRun"/>, which dry-runs halves until each group is one kind. PoracleNG classifies
/// against stored rules only, never between the rules of one batch. A commit that finds
/// a takeover the dry run did not (another write landed in between) puts the rule back from the
/// <c>previous</c> state PoracleNG reports.
/// </para>
/// </remarks>
internal static class IncidentCreateProtocol
{
    /// <summary>Creates <paramref name="rules"/>.</summary>
    /// <param name="single">One rule the user asked for: a takeover is refused rather than skipped.</param>
    /// <param name="profileNo">The profile to write to (<c>?profile=N</c>); null for the active one.</param>
    /// <param name="onExisting">Told the stored rule's uid when a single rule was an exact repeat and PoracleNG named it.</param>
    /// <returns>The uid each rule was created under, index for index; 0 where nothing was written.</returns>
    public static async Task<int[]> CreateAsync(
        IPoracleIncidentProxy proxy, string userId, IReadOnlyList<PokestopEvent> rules, bool single, int? profileNo = null, Action<int>? onExisting = null)
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
                throw new TrackingConflictException(TrackingType, PoracleTrackingProxy.TakeoverMessage)
                {
                    ExistingUid = ConflictingUidOf(dry),
                };
            }

            // An identical rule is already stored; nothing is written.
            if (dry.Unchanged[0].Uid > 0)
            {
                onExisting?.Invoke(dry.Unchanged[0].Uid);
            }

            return uids;
        }
        else if (dry.Created.Count == 0)
        {
            // Nothing in the batch is new, so nothing would be written and no rule needs telling apart.
            return uids;
        }
        else
        {
            keep = (await BatchDryRun.ClassifyAsync(
                rules.Count,
                CountsOf(dry),
                async idx => CountsOf(await Post([.. idx.Select(i => rules[i])], dryRun: true)))).Keep;

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
            throw new TrackingConflictException(TrackingType, PoracleTrackingProxy.TakeoverMessage)
            {
                ExistingUid = ConflictingUidOf(commit),
            };
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
    /// The stored rule the first would-be takeover names: its <c>previous</c> state when PoracleNG reported
    /// one, else the updated rule itself. Null when neither carries a uid.
    /// </summary>
    private static int? ConflictingUidOf(PokestopEventWriteResult result)
    {
        var uid = result.Previous is { Count: > 0 } previous && previous[0] is { Uid: > 0 } stored
            ? stored.Uid
            : result.Updated.Count > 0 ? result.Updated[0].Uid : 0;
        return uid > 0 ? uid : null;
    }

    private static BatchDryRun.Counts CountsOf(PokestopEventWriteResult result) =>
        new(result.Created.Count, result.Updated.Count, result.Unchanged.Count);

    private const string TrackingType = "incident";
}
