using System.Text.Json;
using Microsoft.Extensions.Logging;
using Pgan.PoracleWebNet.Core.Abstractions.Services;

namespace Pgan.PoracleWebNet.Core.Services;

/// <summary>
/// Follows quick-pick tracked uids through a bulk distance update.
/// </summary>
/// <remarks>
/// <para>
/// The bulk distance endpoints are a fetch-mutate-repost, and PoracleNG rewrites each row, so every uid
/// changes. #403 fixed this for single edits, where the create response gives an unambiguous 1:1 mapping.
/// It cannot be done that way here: the batch response comes back <em>reordered</em>. Submitting lures
/// 161, 162, 163 returned 164, 165, 166 mapping to 163, 162, 161 — so pairing by position would repoint a
/// quick pick at somebody else's alarm and its Remove would then delete an alarm the user still wanted.
/// That is worse, and quieter, than the bug being fixed. See #443.
/// </para>
/// <para>
/// Pairing is therefore done on content. A bulk distance update changes exactly one field, so two rows are
/// the same alarm when every other field matches. Where a signature is ambiguous — two rows identical
/// apart from distance — nothing is remapped and it is logged, because guessing is the failure mode this
/// exists to avoid. Such rows are interchangeable anyway once both carry the same distance.
/// </para>
/// </remarks>
public static partial class BulkUidRemap
{
    /// <summary>
    /// Fields that differ between the two snapshots by definition, so they cannot identify a row.
    /// <c>profile_no</c> is here because outgoing payloads no longer carry it (#411) while rows read back
    /// from PoracleNG do - without this the two signatures never match and nothing is ever remapped.
    /// <c>description</c> is PoracleNG&apos;s rendered summary of the row, not part of its identity: it embeds
    /// the distance AND the clean flags, so it changes whenever either does. Leaving it in meant the
    /// cleaning toggle could never pair a row with its replacement.
    /// </summary>
    private static readonly HashSet<string> IgnoredForIdentity =
        new(StringComparer.Ordinal) { "uid", "distance", "profile_no", "description" };

    /// <summary>
    /// Re-reads the tracking rows and moves any quick-pick tracked uid onto its replacement.
    /// Best-effort: the distance update itself has already succeeded, so nothing here may throw.
    /// </summary>
    /// <param name="submitted">The rows as they were posted, still carrying their pre-write uids.</param>
    public static async Task ApplyAsync(
        IPoracleTrackingProxy proxy,
        string trackingType,
        string userId,
        JsonElement submitted,
        ITrackedUidRemapper uidRemapper,
        ILogger logger,
        IReadOnlyCollection<string>? mutatedFields = null)
    {
        try
        {
            // Whatever the caller just changed cannot take part in identity, or the before and after
            // snapshots never match. Distance is always excluded because that is what the distance
            // endpoints change; cleaning passes "clean" for the same reason.
            var identityKeys = IdentityKeys(submitted, mutatedFields);
            if (identityKeys.Count == 0)
            {
                return;
            }

            var before = BuildIndex(submitted, identityKeys);
            if (before.Count == 0)
            {
                return;
            }

            var after = BuildIndex(await proxy.GetByUserAsync(trackingType, userId), identityKeys);

            foreach (var (signature, oldUid) in before)
            {
                if (oldUid is null)
                {
                    // Ambiguous before the write, so there is nothing safe to move.
                    LogAmbiguous(logger, trackingType, signature);
                    continue;
                }

                if (!after.TryGetValue(signature, out var newUid) || newUid is null)
                {
                    LogNoMatch(logger, trackingType, oldUid.Value);
                    continue;
                }

                if (newUid.Value != oldUid.Value)
                {
                    await uidRemapper.RemapAsync(userId, trackingType, oldUid.Value, newUid.Value);
                }
            }
        }
        catch (Exception ex)
        {
            // The distance change is already applied upstream. Failing here would fail a request that
            // worked, to protect a quick pick's Remove button.
            LogRemapFailed(logger, ex, trackingType);
        }
    }

    /// <summary>
    /// Maps each row's identity signature to its uid. A signature seen more than once maps to
    /// <c>null</c>, which means "ambiguous — do not touch".
    /// </summary>
    private static Dictionary<string, int?> BuildIndex(JsonElement rows, IReadOnlyCollection<string> identityKeys)
    {
        var index = new Dictionary<string, int?>(StringComparer.Ordinal);

        if (rows.ValueKind != JsonValueKind.Array)
        {
            return index;
        }

        foreach (var row in rows.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (!row.TryGetProperty("uid", out var uidProp) || uidProp.ValueKind != JsonValueKind.Number)
            {
                continue;
            }

            var signature = Signature(row, identityKeys);
            index[signature] = index.ContainsKey(signature) ? null : uidProp.GetInt32();
        }

        return index;
    }

    /// <summary>
    /// The property names that identify a row, taken from what we submitted.
    /// </summary>
    /// <remarks>
    /// Derived from the outgoing payload rather than fixed, because PoracleNG returns fields we never
    /// send - a rendered <c>description</c>, and <c>profile_no</c> which outgoing writes deliberately omit
    /// (#411). Comparing full property sets therefore never matched anything, which is how the first
    /// version of this shipped green unit tests and did nothing at all in practice.
    /// </remarks>
    private static IReadOnlyCollection<string> IdentityKeys(JsonElement rows, IReadOnlyCollection<string>? mutatedFields)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);

        if (rows.ValueKind != JsonValueKind.Array)
        {
            return keys;
        }

        foreach (var row in rows.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            foreach (var prop in row.EnumerateObject())
            {
                if (!IgnoredForIdentity.Contains(prop.Name) && mutatedFields?.Contains(prop.Name) != true)
                {
                    keys.Add(prop.Name);
                }
            }
        }

        return keys;
    }

    /// <summary>The identity fields, name-sorted so property order cannot affect the result.</summary>
    private static string Signature(JsonElement row, IReadOnlyCollection<string> identityKeys) =>
        string.Join(
            "|",
            identityKeys
                .OrderBy(k => k, StringComparer.Ordinal)
                .Select(k => row.TryGetProperty(k, out var v) ? $"{k}={v.GetRawText()}" : $"{k}=<absent>"));

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Two {TrackingType} rows share an identity after a bulk distance update; leaving quick-pick uids alone rather than guessing.")]
    private static partial void LogAmbiguous(ILogger logger, string trackingType, string signature);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "No replacement row found for {TrackingType} uid {OldUid} after a bulk distance update.")]
    private static partial void LogNoMatch(ILogger logger, string trackingType, int oldUid);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Could not follow quick-pick tracked uids through a bulk {TrackingType} distance update; removing an affected quick pick may leave its alarms behind.")]
    private static partial void LogRemapFailed(ILogger logger, Exception exception, string trackingType);
}
