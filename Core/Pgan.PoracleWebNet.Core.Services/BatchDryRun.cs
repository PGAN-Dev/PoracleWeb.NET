namespace Pgan.PoracleWebNet.Core.Services;

/// <summary>
/// Tells apart the rows of a mixed batch from dry runs whose buckets carry no index back to the request.
/// Shared by <c>PoracleTrackingProxy.CreateCoreAsync</c> and <see cref="IncidentCreateProtocol"/>.
/// </summary>
/// <remarks>
/// A group of rows whose dry run is all new, or has nothing new, needs no further asking: every row in it is
/// the same kind, or none of it is written. Only a mixed group is split, in halves, so a batch of a thousand
/// rows with a few stored ones among them costs a few dozen dry runs rather than a thousand. This rests on
/// PoracleNG classifying each row against stored rules only, never against the other rows of the batch, which
/// is what lets a half's answer stand for those rows. Groups smaller than <see cref="AskAloneBelow"/> ask about
/// each row alone, since halving them saves nothing.
/// </remarks>
internal static class BatchDryRun
{
    private const int AskAloneBelow = 4;

    /// <summary>The size of each bucket in a dry run's answer.</summary>
    internal readonly record struct Counts(int Created, int Updated, int Unchanged);

    /// <summary>The rows to commit, in request order, and what the rest were.</summary>
    internal sealed record Classification(List<int> Keep, int AlreadyPresent, int Takeovers);

    /// <param name="rowCount">The rows in the batch.</param>
    /// <param name="whole">The batch's own dry run, already asked.</param>
    /// <param name="dryRun">Dry-runs the rows at the given indexes.</param>
    /// <param name="onTakeover">Told a would-be takeover's row index, or -1 when it was counted with others.</param>
    public static async Task<Classification> ClassifyAsync(
        int rowCount, Counts whole, Func<IReadOnlyList<int>, Task<Counts>> dryRun, Action<int>? onTakeover = null)
    {
        var keep = new List<int>();
        var present = 0;
        var takeovers = 0;

        async Task Resolve(IReadOnlyList<int> rows, Counts counts)
        {
            if (rows.Count == 1)
            {
                // One row's answer is its own verdict: a takeover outranks, then an exact duplicate. An answer
                // that says nothing is left for the commit, which repairs a takeover it did not foresee.
                if (counts.Updated > 0)
                {
                    takeovers++;
                    onTakeover?.Invoke(rows[0]);
                }
                else if (counts.Unchanged > 0 && counts.Created == 0)
                {
                    present++;
                }
                else
                {
                    keep.Add(rows[0]);
                }

                return;
            }

            if (counts.Updated == 0 && counts.Unchanged == 0)
            {
                keep.AddRange(rows);
                return;
            }

            if (counts.Created == 0)
            {
                // Each row lands in exactly one bucket, so the buckets are the counts.
                present += counts.Unchanged;
                takeovers += counts.Updated;
                for (var t = 0; t < counts.Updated; t++)
                {
                    onTakeover?.Invoke(-1);
                }

                return;
            }

            if (rows.Count < AskAloneBelow)
            {
                foreach (var row in rows)
                {
                    await Resolve([row], await dryRun([row]));
                }

                return;
            }

            var half = rows.Count / 2;
            List<int> first = [.. rows.Take(half)];
            List<int> second = [.. rows.Skip(half)];
            await Resolve(first, await dryRun(first));
            await Resolve(second, await dryRun(second));
        }

        await Resolve([.. Enumerable.Range(0, rowCount)], whole);
        keep.Sort();
        return new Classification(keep, present, takeovers);
    }
}
