using System.Text.Json;
using Microsoft.Extensions.Logging;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

/// <summary>
/// Reconciles a tracking update against what PoracleNG actually did.
/// <para>
/// Updates are sent as a create carrying the existing <c>uid</c>, which PoracleNG normally treats as an
/// upsert. But it dedups each tracking type by a natural key (egg level, raid team/exclusive, quest reward,
/// lure id, and so on), so when an edit changes a field in that key it <em>inserts a new row</em> instead of
/// updating the one the uid points at. The original row survives and the user ends up with two alarms —
/// one still matching their pre-edit filter — while the API reports success.
/// </para>
/// <para>
/// This detects that case from the create response and removes the superseded row. The upsert path is left
/// alone, so the uid only changes when PoracleNG genuinely made a new row.
/// </para>
/// </summary>
internal static partial class TrackingUpdateReconciler
{
    /// <summary>
    /// Deletes the superseded row when PoracleNG inserted rather than updated.
    /// Returns the uid the caller should report back — the new one when a row was inserted, otherwise the original.
    /// </summary>
    public static async Task<int> ReconcileAsync(
        IPoracleTrackingProxy proxy,
        string trackingType,
        string userId,
        int oldUid,
        TrackingCreateResult result,
        ILogger logger,
        JsonElement submitted,
        ITrackedUidRemapper? uidRemapper = null)
    {
        // oldUid <= 0 means this was not an edit, so there is nothing to reconcile.
        if (oldUid <= 0)
        {
            return oldUid;
        }

        // PoracleNG declined to write because the edited values collide with an alarm the user
        // already has. Nothing changed, so reporting success while echoing the requested values back
        // told the user their edit applied when it had not. See #463.
        //
        // But it reports the same {alreadyPresent:1, insert:0, updates:0} when the collision is with the
        // row being edited -- pressing Save with nothing changed, which every edit dialog does by
        // resubmitting the whole form. Reading that as a conflict told the user another alarm was in the
        // way when the only candidate was itself. The two are told apart by asking whether the row at
        // oldUid already holds what was submitted: if it does, the edit is a no-op and there is nothing
        // to report. See #495.
        if (result.AlreadyPresent > 0 && result.Inserts == 0 && result.Updates == 0)
        {
            if (await IsNoOpEditAsync(proxy, trackingType, userId, oldUid, submitted))
            {
                return oldUid;
            }

            throw new TrackingConflictException(
                trackingType,
                "Another alarm of this type already uses those settings. Edit or remove that one instead.");
        }

        if (result.NewUids.Count == 0)
        {
            return oldUid;
        }

        // Trust newUids, not the insert counter. PoracleNG re-keys a row on edit while reporting
        // {"insert":0,"updates":1,"newUids":[<new>]} -- verified directly against it. Gating on
        // Inserts > 0 therefore skipped both the uid correction and the remap for raids, eggs,
        // quests, gyms, fort changes and nests: the PUT answered 200 with a uid that 404s on the very
        // next GET, and any quick pick tracking the row kept pointing at the dead uid. Monsters were
        // unaffected because PoracleNG genuinely updates that type in place. See #460, #464.
        var newUid = (int)result.NewUids[0];
        if (newUid == oldUid)
        {
            return oldUid;
        }

        try
        {
            // Idempotent: when PoracleNG replaced the row in place rather than inserting a duplicate,
            // the old uid is already gone and the proxy swallows the resulting 404.
            await proxy.DeleteByUidAsync(trackingType, userId, oldUid);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // The new row already carries the user's intended settings, so the edit succeeded. Surface the
            // leftover duplicate for triage rather than failing an update that actually applied.
            LogStaleDeleteFailed(logger, ex, trackingType, oldUid, newUid);
        }

        // Quick-pick applied state stores uids captured at apply time; follow the row. See #403.
        if (uidRemapper != null)
        {
            await uidRemapper.RemapAsync(userId, trackingType, oldUid, newUid);
        }

        return newUid;
    }

    /// <summary>
    /// Refuses an edit that PoracleNG would satisfy by merging into a DIFFERENT alarm.
    /// </summary>
    /// <remarks>
    /// PoracleNG decides what to do with a submitted row by diffing it against the existing ones
    /// (diffTracking in processor/internal/api/tracking.go). When the only differences are in fields it
    /// tags <c>diff:"update"</c>, it updates that existing row in place and re-keys it -- answering
    /// {insert:0, updates:1, newUids:[new]}. If the row it picked is not the one being edited, the edit
    /// has just overwritten somebody else's alarm, and the reconciler below then deleted the original as
    /// "superseded": two alarms became one, the victim's radius replaced by the editor's, reported as a
    /// clean 200. Reachable from the ordinary edit dialogs -- changing a raid's team, a gym's slot or
    /// battle toggles, an egg's level, a fort-change's change types. Lures and invasions were never
    /// exposed because they carry their own pre-flight checks from #462.
    /// <para>
    /// The updatable set is uniform upstream -- template, distance and clean, plus slot_changes and
    /// battle_changes on gyms -- so the collision test is the same for every type: equal on everything
    /// else means PoracleNG will merge them.
    /// </para>
    /// </remarks>
    public static async Task EnsureNoMergeIntoAnotherAlarmAsync(
        IPoracleTrackingProxy proxy,
        string trackingType,
        string userId,
        int oldUid,
        JsonElement submitted)
    {
        if (oldUid <= 0 || submitted.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        JsonElement rows;
        try
        {
            rows = await proxy.GetByUserAsync(trackingType, userId);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Cannot see the siblings, so cannot rule a collision in or out. Let the edit through rather
            // than refuse a legitimate one on a transport error; the pre-existing behaviour applies.
            return;
        }

        if (rows.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var row in rows.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object
                || !row.TryGetProperty("uid", out var uid)
                || uid.ValueKind != JsonValueKind.Number
                || uid.GetInt32() == oldUid)
            {
                continue;
            }

            if (WouldMergeInto(submitted, row, trackingType))
            {
                throw new TrackingConflictException(
                    trackingType,
                    "Another alarm of this type already uses those settings. Edit or remove that one instead.");
            }
        }
    }

    /// <summary>Fields the comparison ignores entirely: PoracleNG owns them.</summary>
    private static readonly HashSet<string> AssignedByPoracle = new(StringComparer.Ordinal)
    {
        // Assigned by PoracleNG, or rendered by it from the rest.
        "uid", "id", "profile_no", "description",
        // Never persisted (see #494).
        "ping",
    };

    /// <summary>diff:"update" upstream: a difference here can make PoracleNG merge rather than insert.</summary>
    private static readonly HashSet<string> UpdatableFields = new(StringComparer.Ordinal)
    {
        "distance", "template", "clean",
    };

    // slot_changes and battle_changes were treated as updatable for gyms, on the strength of the tags in
    // the PoracleNG checkout. The running binary keeps two gyms that differ only in those toggles as
    // separate rows -- so they identify an alarm, and calling them updatable refused every edit on both.
    // They are compared like any other field now. See #553.

    private static bool WouldMergeInto(JsonElement submitted, JsonElement existing, string trackingType)
    {
        var updatableDifferences = 0;

        foreach (var field in submitted.EnumerateObject())
        {
            if (AssignedByPoracle.Contains(field.Name))
            {
                continue;
            }

            // A field the stored row does not carry cannot tell the two apart.
            if (!existing.TryGetProperty(field.Name, out var storedValue))
            {
                continue;
            }

            // Compare what PoracleNG will STORE, not what was sent: it rewrites some values on the way
            // in, and it diffs the rewritten row. Comparing the raw submission made every collision
            // look like a difference, which is exactly how the destructive merge got through.
            var same = SameValue(NormalizeForStorage(field, submitted, trackingType), storedValue);

            if (IsUpdatable(field.Name))
            {
                if (!same)
                {
                    updatableDifferences++;
                }

                continue;
            }

            if (!same)
            {
                return false;
            }
        }

        // Counted, not ignored. The running PoracleNG merges two rows only when EXACTLY ONE updatable
        // field differs; with two or more it inserts a separate row, so those alarms genuinely coexist.
        // Ignoring the fields wholesale called every such pair a collision and refused every ordinary edit
        // on both -- radius, template, auto-delete, clearing the gym -- leaving them uneditable. That is a
        // worse defect than the merge this check exists to prevent. See #553.
        //
        // Zero differences is the row colliding with itself, which IsNoOpEditAsync handles; only the
        // one-difference case is a genuine merge into someone else.
        return updatableDifferences <= 1;
    }

    private static bool IsUpdatable(string fieldName) => UpdatableFields.Contains(fieldName);
    /// <summary>
    /// True when the row being edited already holds every value the update submitted, so PoracleNG's
    /// "already present" was the row colliding with itself rather than with a different alarm.
    /// </summary>
    /// <remarks>
    /// A missing row is not a no-op: something else removed it, and the caller should hear about the
    /// conflict rather than be told the edit applied. Fields PoracleNG does not persist are excluded --
    /// otherwise a ping-only edit, which PoracleNG drops on every tracking type, looks like a change and
    /// reads as a conflict.
    /// </remarks>
    private static async Task<bool> IsNoOpEditAsync(
        IPoracleTrackingProxy proxy,
        string trackingType,
        string userId,
        int oldUid,
        JsonElement submitted)
    {
        if (submitted.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        JsonElement rows;
        try
        {
            rows = await proxy.GetByUserAsync(trackingType, userId);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Cannot tell the two cases apart, so keep the conservative answer: report the conflict.
            return false;
        }

        if (rows.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var row in rows.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object
                || !row.TryGetProperty("uid", out var uid)
                || uid.ValueKind != JsonValueKind.Number
                || uid.GetInt32() != oldUid)
            {
                continue;
            }

            return MatchesStoredRow(submitted, row);
        }

        return false;
    }

    /// <summary>Fields the comparison ignores, because the stored row never reflects them.</summary>
    private static readonly HashSet<string> IgnoredForNoOp = new(StringComparer.Ordinal)
    {
        // Assigned by PoracleNG, or rendered by it from the other fields.
        "uid", "profile_no", "description",
        // Never persisted, on any tracking type -- verified directly against PoracleNG. An edit that
        // changes only this therefore leaves the row untouched, which is a no-op, not a conflict.
        "ping",
    };

    private static bool MatchesStoredRow(JsonElement submitted, JsonElement stored)
    {
        foreach (var field in submitted.EnumerateObject())
        {
            if (IgnoredForNoOp.Contains(field.Name))
            {
                continue;
            }

            // A field the stored row does not carry cannot have changed it.
            if (!stored.TryGetProperty(field.Name, out var storedValue))
            {
                continue;
            }

            if (!SameValue(field.Value, storedValue))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Equality that tolerates the shapes PoracleNG stores a value in.
    /// </summary>
    /// <remarks>
    /// A list is written as an array and stored as its JSON text -- fort-change <c>change_types</c> comes
    /// back as the string <c>["name"]</c> -- which is why the models carry StringOrArrayConverter. A byte
    /// comparison would call that unchanged list a change.
    /// </remarks>
    /// <summary>
    /// Applies the rewrites PoracleNG performs before it stores a submitted field.
    /// </summary>
    /// <remarks>
    /// Raids and max battles force <c>level</c> to 9000 unless the alarm tracks any boss
    /// (trackingRaid.go:217-219, trackingMaxbattle.go:137-139). Nothing else in the identity set is
    /// rewritten -- the updatable fields are excluded from the comparison already.
    /// </remarks>
    private static JsonElement NormalizeForStorage(JsonProperty field, JsonElement submitted, string trackingType)
    {
        var levelIsForced = string.Equals(field.Name, "level", StringComparison.Ordinal)
            && (string.Equals(trackingType, "raid", StringComparison.Ordinal)
                || string.Equals(trackingType, "maxbattle", StringComparison.Ordinal))
            && submitted.TryGetProperty("pokemon_id", out var pokemonId)
            && pokemonId.ValueKind == JsonValueKind.Number
            && pokemonId.GetInt32() != AnyPokemonId;

        return levelIsForced ? AnyLevelElement : field.Value;
    }

    private const int AnyPokemonId = 9000;

    private static readonly JsonElement AnyLevelElement =
        JsonDocument.Parse("9000").RootElement.Clone();

    private static bool SameValue(JsonElement submitted, JsonElement stored)
    {
        if (JsonElement.DeepEquals(submitted, stored))
        {
            return true;
        }

        // null and "" are the same absence: the models use null for "any gym", PoracleNG stores "".
        if (IsBlank(submitted) && IsBlank(stored))
        {
            return true;
        }

        if (stored.ValueKind == JsonValueKind.String && TryParse(stored.GetString(), out var reparsed))
        {
            return JsonElement.DeepEquals(submitted, reparsed);
        }

        return false;
    }

    private static bool IsBlank(JsonElement value) =>
        value.ValueKind == JsonValueKind.Null
        || (value.ValueKind == JsonValueKind.String && string.IsNullOrEmpty(value.GetString()));

    private static bool TryParse(string? text, out JsonElement parsed)
    {
        parsed = default;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        try
        {
            parsed = JsonDocument.Parse(text).RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Failed to delete superseded {TrackingType} uid {OldUid} after the edit created uid {NewUid}; a duplicate row may remain.")]
    private static partial void LogStaleDeleteFailed(ILogger logger, Exception exception, string trackingType, int oldUid, int newUid);
}
