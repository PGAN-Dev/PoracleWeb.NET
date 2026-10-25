using System.Text.Json;
using Microsoft.Extensions.Logging;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

/// <summary>
/// A bulk "Update Distance": which of the selected rules a radius can apply to, the v2 <c>PATCH</c> that sets
/// it on them, and what to tell the user about the rest.
/// </summary>
/// <remarks>
/// <para>
/// PoracleNG refuses two scope combinations: an alarm limited to areas that also carries a radius, and one
/// measured from a saved place that carries none. Those are split off here before anything is sent and named
/// in the result, as they always were. "Set these to 2 km" is not a request to widen an alarm the user
/// confined to one neighbourhood, and quietly doing so would be worse than leaving it alone and saying so.
/// </para>
/// <para>
/// The rest go in one <c>PATCH</c> naming the uids and the radius alone, so nothing else a rule holds is
/// re-sent and nothing PoracleWeb.NET does not model can be lost (#730). Each rule keeps its uid, so nothing
/// tracking one (quick-pick applied state) has to follow it. PoracleNG decides for itself which rules would
/// collide with another at the new radius, in the order the uids are given, and refuses those rather than
/// merging them (#580, #598); they come back as <see cref="DistanceUpdateResult.SkippedConflict"/>.
/// </para>
/// </remarks>
internal static partial class DistanceRewrite
{
    /// <summary>One stored rule's uid and the scope that decides whether a radius can apply to it.</summary>
    public readonly record struct RowScope(int Uid, bool HasAreas, bool HasPlace);

    /// <summary>
    /// The selected rules split by scope for <paramref name="Distance"/>: the ones to send, and the ones skipped
    /// and why.
    /// </summary>
    public sealed record DistancePlan(
        int Distance,
        IReadOnlyList<RowScope> Writable,
        IReadOnlyList<int> AreaScoped,
        IReadOnlyList<int> PlaceScoped)
    {
        public IReadOnlyList<int> WritableUids => [.. this.Writable.Select(r => r.Uid).Distinct()];
    }

    /// <summary>
    /// The whole bulk distance for one type over its stored rows: split, one <c>PATCH</c>, report.
    /// </summary>
    public static async Task<DistanceUpdateResult> ApplyAsync(
        IPoracleTrackingProxy proxy,
        string type,
        string userId,
        JsonElement rows,
        Func<JsonElement, bool> include,
        int distance,
        ILogger logger)
    {
        var plan = Plan(ScopesOf(rows, include), distance);
        if (plan.Writable.Count == 0)
        {
            return Report(plan, type, [], logger);
        }

        var result = await proxy.PatchAsync(type, userId, plan.WritableUids, new TrackingPatchSet(Distance: distance));
        return Report(plan, type, [result], logger);
    }

    /// <summary>The scope of every stored row <paramref name="include"/> keeps. A row with no uid cannot be named.</summary>
    public static List<RowScope> ScopesOf(JsonElement rows, Func<JsonElement, bool> include)
    {
        if (rows.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return
        [
            .. rows.EnumerateArray()
                .Where(include)
                .Select(row => (row, uid: PoracleJsonHelper.UidOf(row)))
                .Where(x => x.uid is > 0)
                .Select(x => new RowScope(x.uid!.Value, HasAreas(x.row), HasPlace(x.row))),
        ];
    }

    /// <summary>Splits the selected rules into the ones a radius can apply to and the ones it cannot.</summary>
    public static DistancePlan Plan(IEnumerable<RowScope> rows, int distance)
    {
        var writable = new List<RowScope>();
        var areaScoped = new List<int>();
        var placeScoped = new List<int>();

        foreach (var row in rows)
        {
            switch (ConflictOf(row.HasAreas, row.HasPlace, distance))
            {
                case ScopeConflict.AreaScoped:
                    areaScoped.Add(row.Uid);
                    break;
                case ScopeConflict.PlaceScoped:
                    placeScoped.Add(row.Uid);
                    break;
                default:
                    writable.Add(row);
                    break;
            }
        }

        return new DistancePlan(distance, writable, areaScoped, placeScoped);
    }

    /// <summary>
    /// What the user is told: rules PoracleNG updated or found already at that radius count as updated; its
    /// refusals are reported by reason.
    /// </summary>
    /// <remarks>
    /// <c>invalid</c> is put down to the rule's scope only when the scope explains it, which for a rule that was
    /// sent it never should: <see cref="Plan"/> sent it because its scope takes this radius. Anything else has no
    /// reason to give the user, so it is logged and reported with the conflicts rather than dropped or labelled
    /// with a scope that is not the cause. <c>not_found</c> is a rule gone between the read and the write:
    /// logged, not reported.
    /// </remarks>
    public static DistanceUpdateResult Report(
        DistancePlan plan, string type, IReadOnlyList<TrackingPatchResult> results, ILogger logger)
    {
        // Two stored rules can share a uid (an invasion and a Pokestop event are numbered separately), so the first wins.
        var scopes = new Dictionary<int, RowScope>();
        foreach (var row in plan.Writable)
        {
            scopes.TryAdd(row.Uid, row);
        }

        var areaScoped = plan.AreaScoped.ToList();
        var placeScoped = plan.PlaceScoped.ToList();
        var conflict = new List<int>();
        var updated = 0;

        foreach (var result in results)
        {
            updated += result.Updated.Count + result.Unchanged.Count;

            foreach (var refusal in result.Refused)
            {
                switch (refusal.Reason)
                {
                    case PatchRefusalReasons.Conflict:
                        conflict.Add(refusal.Uid);
                        break;

                    case PatchRefusalReasons.Invalid when ScopeConflictOf(scopes, refusal.Uid, plan.Distance) == ScopeConflict.AreaScoped:
                        areaScoped.Add(refusal.Uid);
                        break;

                    case PatchRefusalReasons.Invalid when ScopeConflictOf(scopes, refusal.Uid, plan.Distance) == ScopeConflict.PlaceScoped:
                        placeScoped.Add(refusal.Uid);
                        break;

                    case PatchRefusalReasons.NotFound:
                        LogRefusalNotFound(logger, type, refusal.Uid, refusal.Detail);
                        break;

                    default:
                        LogRefusalUnexplained(logger, type, refusal.Uid, refusal.Reason, refusal.Detail);
                        conflict.Add(refusal.Uid);
                        break;
                }
            }
        }

        return new DistanceUpdateResult(updated, areaScoped, placeScoped, conflict);
    }

    private static ScopeConflict ScopeConflictOf(Dictionary<int, RowScope> scopes, int uid, int distance) =>
        scopes.TryGetValue(uid, out var scope) ? ConflictOf(scope.HasAreas, scope.HasPlace, distance) : ScopeConflict.None;

    /// <summary>Why a radius cannot be applied to an alarm with this scope, if it cannot.</summary>
    public static ScopeConflict ConflictOf(bool hasAreas, bool hasPlace, int distance) =>
        (hasAreas, hasPlace, distance) switch
        {
            (true, _, > 0) => ScopeConflict.AreaScoped,
            (false, true, 0) => ScopeConflict.PlaceScoped,
            _ => ScopeConflict.None,
        };

    public enum ScopeConflict
    {
        None,
        AreaScoped,
        PlaceScoped,
    }

    /// <summary>
    /// Whether the stored row is limited to areas. PoracleNG reads no override back as <c>null</c>; the
    /// JSON-text form is tolerated for the same reason the merge guard tolerates it.
    /// </summary>
    private static bool HasAreas(JsonElement row)
    {
        if (!row.TryGetProperty("override_areas", out var areas))
        {
            return false;
        }

        if (areas.ValueKind == JsonValueKind.String)
        {
            var text = areas.GetString();
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            try
            {
                using var doc = JsonDocument.Parse(text);
                return doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        return areas.ValueKind == JsonValueKind.Array && areas.GetArrayLength() > 0;
    }

    private static bool HasPlace(JsonElement row) => PlaceUsage.HasPlace(row);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Bulk distance {Type}: uid={Uid} was not found when patched ({Detail}); it went between the read and the write.")]
    private static partial void LogRefusalNotFound(ILogger logger, string type, int uid, string detail);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Bulk distance {Type}: PoracleNG refused uid={Uid} as {Reason} ({Detail}) and its scope does not account for it; reported as a conflict.")]
    private static partial void LogRefusalUnexplained(ILogger logger, string type, int uid, string reason, string detail);
}
