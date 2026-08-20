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
    /// Refuses a bulk write in which a submitted row would take over a row that was NOT submitted.
    /// </summary>
    /// <remarks>
    /// The batch check only looks at the rows being written. At the new radius a selected row can differ
    /// from an unselected sibling by exactly one updatable field, and PoracleNG then rewrites the sibling --
    /// an alarm the user never selected -- while the selected one keeps its old radius and the response
    /// reports it updated. See #598.
    /// </remarks>
    public static async Task EnsureBatchDoesNotTakeOverOthersAsync(
        IPoracleTrackingProxy proxy,
        string trackingType,
        string userId,
        JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var submittedUids = new HashSet<int>();
        foreach (var row in body.EnumerateArray())
        {
            if (row.ValueKind == JsonValueKind.Object
                && row.TryGetProperty("uid", out var uid)
                && uid.ValueKind == JsonValueKind.Number)
            {
                submittedUids.Add(uid.GetInt32());
            }
        }

        JsonElement existing;
        try
        {
            existing = await proxy.GetByUserAsync(trackingType, userId);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return;
        }

        if (existing.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var candidate in body.EnumerateArray())
        {
            // Ordering matters here too: PoracleNG resolves each candidate against the FIRST row that
            // classifies, so a later sibling is never touched. Checking every row refused bulk changes that
            // would have been harmless -- and told the user to make the single change that was also
            // refused. See #606.
            var decisive = FirstClassifyingRow(existing, candidate, trackingType);
            if (decisive is { Classification: RowMatch.Update } match
                && !submittedUids.Contains(match.Uid))
            {
                throw new TrackingConflictException(
                    trackingType,
                    "That radius would overwrite another alarm you did not select. "
                    + "Remove or edit that alarm first.");
            }
        }
    }

    /// <summary>
    /// Refuses a bulk write in which two of the submitted rows would become the same alarm.
    /// </summary>
    /// <remarks>
    /// The distance endpoints rewrite every selected row and POST them as one batch. Two rows that differed
    /// only by radius become identical once both are set to the same radius, and PoracleNG resolves that
    /// within the batch -- so the user ended up with FEWER alarms than they selected, at least one still at
    /// its old radius, and a response claiming every one was updated. Refuse instead of silently losing a
    /// row. See #580.
    /// </remarks>
    public static void EnsureBatchDoesNotCollapse(JsonElement body, string trackingType)
    {
        if (body.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in body.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (!seen.Add(IdentityOf(row)))
            {
                throw new TrackingConflictException(
                    trackingType,
                    "Two of the selected alarms would end up identical at that radius. "
                    + "Change them separately, or remove one first.");
            }
        }
    }

    /// <summary>Everything about a row that distinguishes it from another, in a stable order.</summary>
    private static string IdentityOf(JsonElement row) =>
        string.Join(
            '|',
            row.EnumerateObject()
                .Where(p => !AssignedByPoracle.Contains(p.Name))
                .OrderBy(p => p.Name, StringComparer.Ordinal)
                .Select(p => $"{p.Name}={p.Value}"));
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
        if (submitted.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        // Pokemon edits cannot merge at all: trackingMonster.go splits rows on req.UID.isSet() and sends
        // uid-bearing ones straight to UpdateMonsterByUID, never reaching the diff. Every refusal this
        // guard produced for a monster edit was therefore false. It is the only type that does this.
        // See #606.
        if (oldUid > 0 && string.Equals(trackingType, "pokemon", StringComparison.Ordinal))
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
            return;
        }

        if (rows.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        // In list order, and stopping at the first row that classifies. DiffAndClassify walks the
        // existing rows and BREAKS on the first duplicate-or-update match, so only that row is ever
        // taken over -- a later sibling is irrelevant. Refusing on any match anywhere blocked ordinary
        // radius and template edits whenever a similar alarm existed further down the list, and the
        // error told the user to do the very thing that was blocked. See #606.
        var decisive = FirstClassifyingRow(rows, submitted, trackingType);
        if (decisive is not { Classification: RowMatch.Update } match)
        {
            return;
        }

        if (match.Uid == oldUid)
        {
            // PoracleNG would update the row being edited, which is exactly what was asked for.
            return;
        }

        throw new TrackingConflictException(
            trackingType,
            "Another alarm of this type already uses those settings. Edit or remove that one instead.");
    }

    private enum RowMatch
    {
        Duplicate,
        Update,
    }

    private readonly record struct ClassifiedRow(int Uid, RowMatch Classification);

    /// <summary>
    /// The first existing row PoracleNG would resolve the submission against, mirroring the break in
    /// DiffAndClassify. Rows it would treat as unrelated, or as a separate insert, are skipped.
    /// </summary>
    private static ClassifiedRow? FirstClassifyingRow(
        JsonElement rows, JsonElement submitted, string trackingType)
    {
        foreach (var row in rows.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object
                || !row.TryGetProperty("uid", out var uid)
                || uid.ValueKind != JsonValueKind.Number)
            {
                continue;
            }

            var differences = CountUpdatableDifferences(submitted, row, trackingType);
            if (differences is null)
            {
                // An identity field differs: unrelated, or a separate insert. Keep looking.
                continue;
            }

            if (differences == 0)
            {
                return new ClassifiedRow(uid.GetInt32(), RowMatch.Duplicate);
            }

            if (differences == 1)
            {
                return new ClassifiedRow(uid.GetInt32(), RowMatch.Update);
            }
        }

        return null;
    }
    /// <summary>Fields the comparison ignores entirely: PoracleNG owns them.</summary>
    private static readonly HashSet<string> AssignedByPoracle = new(StringComparer.Ordinal)
    {
        // Assigned by PoracleNG, or rendered by it from the rest.
        "uid", "id", "profile_no", "description",
        // Never persisted (see #494).
        "ping",
    };

    /// <summary>
    /// The <c>diff:"update"</c> fields, per tracking type, transcribed from the structs in
    /// <c>processor/internal/db/tracking_queries.go</c> at the commit prod runs.
    /// </summary>
    /// <remarks>
    /// Per type, not a shared set with exceptions. Assuming a common {distance, template, clean} missed
    /// that monsters also tag <c>min_iv</c>, so adding the same Pokemon at a tighter IV floor was let
    /// through and PoracleNG took the existing alarm over -- 201 Created over the row it had just
    /// destroyed. Forts have no clean column at all. When PoracleNG is upgraded, re-read those tags
    /// before assuming this still holds. See #574.
    /// <para>
    /// Re-read at PoracleNG 5.1.0 (c5e08cb4), the version prod runs since 2026-08-18: every entry below
    /// still matches. 5.1.0 added <c>override_location_label</c> and <c>override_areas</c> to all ten
    /// types tagged <c>diff:""</c>, which is the untagged identity behaviour, so they need no entry here
    /// -- but they DO have to reach the comparison, which is why the write paths merge the stored row in
    /// before these guards run. See #730.
    /// </para>
    /// </remarks>
    private static readonly Dictionary<string, HashSet<string>> UpdatableFieldsByType =
        new(StringComparer.Ordinal)
        {
            ["pokemon"] = new(StringComparer.Ordinal) { "clean", "distance", "min_iv", "template" },
            ["gym"] = new(StringComparer.Ordinal)
            {
                "battle_changes", "clean", "distance", "slot_changes", "template",
            },
            ["fort"] = new(StringComparer.Ordinal) { "distance", "template" },
            ["raid"] = new(StringComparer.Ordinal) { "clean", "distance", "template" },
            ["egg"] = new(StringComparer.Ordinal) { "clean", "distance", "template" },
            ["quest"] = new(StringComparer.Ordinal) { "clean", "distance", "template" },
            ["invasion"] = new(StringComparer.Ordinal) { "clean", "distance", "template" },
            ["lure"] = new(StringComparer.Ordinal) { "clean", "distance", "template" },
            ["nest"] = new(StringComparer.Ordinal) { "clean", "distance", "template" },
        };

    /// <summary>Types PoracleNG does not diff at all fall back to the common three.</summary>
    /// <remarks>
    /// Unreached today. Maxbattle is the only type missing from the dictionary above, and it tags nothing
    /// <c>diff:"update"</c> upstream, so this fallback would over-predict merges for it -- except
    /// MaxBattleService never calls the reconciler: it deletes and re-creates, and pre-checks siblings
    /// with its own IsSameAlarm. Verified at 5.1.0 rather than assumed. Left as the conservative default
    /// for whatever type is added next.
    /// </remarks>
    private static readonly HashSet<string> DefaultUpdatableFields = new(StringComparer.Ordinal)
    {
        "clean", "distance", "template",
    };



    /// <summary>
    /// Counts differing updatable fields, or null when an identity field differs (no relation).
    /// </summary>
    private static int? CountUpdatableDifferences(
        JsonElement submitted, JsonElement existing, string trackingType)
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

            // template needs the value PoracleNG will STORE, not the one we sent. Both previous attempts
            // at this were wrong in opposite directions: counting a blank as a difference made the check
            // miss real collisions (#561), and skipping it entirely made an Add that differs from an
            // existing alarm only by that alarm's custom template read as zero differences -- so it was
            // let through, and PoracleNG overwrote the custom template (#593).
            //
            // Substituting the default resolves both: a blank submission is compared as the default
            // PoracleNG would fill in, so it matches a stored default and differs from a stored custom
            // template, which is exactly what PoracleNG's own diff does.
            var submittedValue = field.Value;
            if (string.Equals(field.Name, "template", StringComparison.Ordinal) && IsBlank(submittedValue))
            {
                submittedValue = DefaultTemplateElement;
            }

            // Compare what PoracleNG will STORE, not what was sent: it rewrites some values on the way
            // in, and it diffs the rewritten row. Comparing the raw submission made every collision
            // look like a difference, which is exactly how the destructive merge got through.
            var same = SameValue(NormalizeForStorage(field, submittedValue, submitted, trackingType), storedValue);

            if (IsUpdatable(field.Name, trackingType))
            {
                if (!same)
                {
                    updatableDifferences++;
                }

                continue;
            }

            if (!same)
            {
                return null;
            }
        }

        // Mirrors DiffTracking in processor/internal/db/diff.go at the commit prod runs:
        //
        //     if totalDiffs == 0                            -> duplicate (nothing written)
        //     if totalDiffs == 1 && nonUpdatableDiffs == 0  -> UPDATE of that existing row
        //     otherwise                                     -> new insert
        //
        // Counted, not ignored. Ignoring the updatable fields wholesale called every pair that differs in
        // two of them a collision, and refused every ordinary edit on both -- radius, template,
        // auto-delete, clearing the gym -- leaving them uneditable (#553).
        //
        return updatableDifferences;
    }

    private static bool IsUpdatable(string fieldName, string trackingType) =>
        (UpdatableFieldsByType.TryGetValue(trackingType, out var fields) ? fields : DefaultUpdatableFields)
            .Contains(fieldName);
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
    private static JsonElement NormalizeForStorage(
        JsonProperty field, JsonElement submittedValue, JsonElement submitted, string trackingType)
    {
        var levelIsForced = string.Equals(field.Name, "level", StringComparison.Ordinal)
            && (string.Equals(trackingType, "raid", StringComparison.Ordinal)
                || string.Equals(trackingType, "maxbattle", StringComparison.Ordinal))
            && submitted.TryGetProperty("pokemon_id", out var pokemonId)
            && pokemonId.ValueKind == JsonValueKind.Number
            && pokemonId.GetInt32() != AnyPokemonId;

        return levelIsForced ? AnyLevelElement : submittedValue;
    }

    /// <summary>
    /// The template PoracleNG stores when a submission leaves it blank.
    /// </summary>
    /// <remarks>
    /// PoracleNG uses <c>general.defaultTemplateName</c> and falls back to <c>"1"</c> when it is unset
    /// (trackingMonster.go and its siblings). PoracleWeb does not read that setting here, so a deployment
    /// that configures a custom default would see an exact-duplicate Add answered 409 rather than 200.
    /// The durable fix is for the alarm services to send the resolved default instead of a blank; this
    /// constant matches the upstream fallback in the meantime. See #593.
    /// </remarks>
    private static readonly JsonElement DefaultTemplateElement =
        JsonDocument.Parse("\"1\"").RootElement.Clone();

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
