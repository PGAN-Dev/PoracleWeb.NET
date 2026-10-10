using System.Text.Json;
using Microsoft.Extensions.Logging;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

public partial class InvasionService(IPoracleTrackingProxy proxy, IFeatureGate featureGate, ILogger<InvasionService> logger, ITrackedUidRemapper uidRemapper, INaturalKeyCapabilityService naturalKeys) : IInvasionService
{
    private const string TrackingType = "invasion";
    private readonly IPoracleTrackingProxy _proxy = proxy;
    private readonly IFeatureGate _featureGate = featureGate;
    private readonly ILogger<InvasionService> _logger = logger;
    private readonly ITrackedUidRemapper _uidRemapper = uidRemapper;
    private readonly INaturalKeyCapabilityService _naturalKeys = naturalKeys;

    /// <summary>
    /// Whether pokestop-event rows belong to the Pokestop Events page rather than this list.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Invasion and incident rows share the <c>invasion</c> table, and PoracleWeb reads invasions over
    /// v1, which applies no filter — verified against 5.2.1, a Showcase rule created through
    /// <c>/api/v2/.../tracking/incident</c> comes straight back out of
    /// <c>GET /api/tracking/invasion/{id}</c>. So this list has to do the partition PoracleNG does for
    /// the v2 endpoints.
    /// </para>
    /// <para>
    /// <strong>Conditional, and that is the whole point.</strong> When the Pokestop Events surface is
    /// unavailable — an older PoracleNG, or an operator who set <c>disable_showcase</c> — the page that
    /// would hold these rows does not exist, and filtering them out here would leave alarms that fire
    /// and cannot be seen or deleted. Event rows are creatable from the invasion add dialog on 5.1.0
    /// today, so this is not hypothetical.
    /// </para>
    /// </remarks>
    private Task<bool> EventsLiveElsewhereAsync() =>
        this._featureGate.IsEnabledAsync(DisableFeatureKeys.PokestopEvents);

    /// <summary>
    /// The same partition as <see cref="ReadOwnRowsAsync"/>, over a raw stored row, for the bulk
    /// paths that rewrite rows in place instead of round-tripping the model.
    /// </summary>
    private async Task<Func<JsonElement, bool>> OwnRowPredicateAsync()
    {
        if (!await this.EventsLiveElsewhereAsync())
        {
            return _ => true;
        }

        return row => !PokestopEventTypes.IsEventName(
            row.TryGetProperty("grunt_type", out var gt) && gt.ValueKind == JsonValueKind.String
                ? gt.GetString()
                : null);
    }

    private async Task<List<Invasion>> ReadOwnRowsAsync(string userId)
    {
        var json = await this._proxy.GetByUserAsync(TrackingType, userId);
        var items = DeserializeItems(json);

        return await this.EventsLiveElsewhereAsync()
            ? [.. items.Where(x => !PokestopEventTypes.IsEventName(x.GruntType))]
            : items;
    }

    /// <inheritdoc />
    public async Task<bool> BelongsToPokestopEventsAsync(string? gruntType) =>
        PokestopEventTypes.IsEventName(gruntType) && await this.EventsLiveElsewhereAsync();

    public async Task<IEnumerable<Invasion>> GetByUserAsync(string userId, int profileNo) =>
        await this.ReadOwnRowsAsync(userId);

    public async Task<Invasion?> GetByUidAsync(string userId, int uid)
    {
        var items = await this.ReadOwnRowsAsync(userId);
        return items.FirstOrDefault(x => x.Uid == uid);
    }

    public async Task<Invasion> CreateAsync(string userId, Invasion model)
    {
        await this._featureGate.EnsureEnabledAsync(DisableFeatureKeys.Invasions);
        model.Id = userId;
        RequireGruntType(model);

        var body = SerializeToElement(model);

        if (await this._naturalKeys.IsEnforcedAsync(TrackingType))
        {
            // Up to PoracleNG 5.1.0 the table carries a unique key on (id, profile_no, gender, grunt_type),
            // case-insensitive at the database, so creating "Water" alongside an existing "water" -- or the
            // same pair with any other difference -- hit a duplicate-key error and came back as a 500.
            // See #500.
            var siblings = await this.GetByUserAsync(userId, model.ProfileNo);
            if (siblings.Any(x => x.Gender == model.Gender
                && string.Equals(x.GruntType, model.GruntType, StringComparison.OrdinalIgnoreCase)))
            {
                throw new TrackingConflictException(
                    TrackingType,
                    "You already have an invasion alarm for that grunt type and gender. Edit or remove that one instead.");
            }
        }
        else
        {
            // Migration 8 (5.2.x) dropped that key. PoracleNG now stores a second rule for one grunt type
            // and gender beside an area-scoped twin, a place-measured one, one two updatable fields away,
            // or one spelled in another case, so the only Add refused is the one it would resolve into an
            // existing rule: the guard the other seven types use. It reads the raw rows rather than this
            // page's list, because PoracleNG diffs against pokestop-event rows in the same table too.
            await TrackingUpdateReconciler.EnsureNoMergeIntoAnotherAlarmAsync(
                this._proxy, TrackingType, userId, 0, body);
        }
        var result = await this._proxy.CreateAsync(TrackingType, userId, body);

        if (result.NewUids.Count > 0)
        {
            model.Uid = (int)result.NewUids[0];
        }

        return model;
    }

    public async Task<Invasion> UpdateAsync(string userId, Invasion model)
    {
        await this._featureGate.EnsureEnabledAsync(DisableFeatureKeys.Invasions);
        RequireGruntType(model);
        var oldUid = model.Uid;
        var keyEnforced = await this._naturalKeys.IsEnforcedAsync(TrackingType);

        // Where the table still carries the natural unique key (5.1.0), PoracleNG's create has no upsert
        // path, so changing a field outside that key collides (Error 1062) and returns 500; the row is
        // replaced instead, below. Refuse a collision BEFORE the delete: editing one onto a
        // (gender, grunt_type) pair another alarm already holds made the replace merge into that alarm -
        // this one deleted, the other one silently overwritten. Changing the gender dropdown is enough
        // to trigger it. See #462.
        if (keyEnforced && oldUid > 0)
        {
            var siblings = await this.GetByUserAsync(userId, model.ProfileNo);
            if (siblings.Any(x => x.Uid != oldUid
                && x.Gender == model.Gender
                && string.Equals(x.GruntType, model.GruntType, StringComparison.OrdinalIgnoreCase)))
            {
                throw new TrackingConflictException(
                    TrackingType,
                    "You already have an invasion alarm for that grunt type and gender. Edit or remove that one instead.");
            }
        }

        var body = SerializeToElement(model);

        // Carry forward anything the stored row holds that the model does not declare. See #730.
        body = await TrackingFieldPreserver.PreserveStoredFieldsAsync(
            this._proxy, TrackingType, userId, oldUid, body);

        if (!keyEnforced)
        {
            // Without the key an invasion is an ordinary type: refuse only the edit PoracleNG would merge
            // into a different rule, as the other seven do. Refusing on the pair alone would leave a rule
            // with an area-scoped twin uneditable, the #553 shape.
            await TrackingUpdateReconciler.EnsureNoMergeIntoAnotherAlarmAsync(
                this._proxy, TrackingType, userId, oldUid, body);
        }

        // /api/v2's PUT is addressed by uid and replaces the row rather than inserting beside it, so the
        // natural key is never in contention and the delete-create-restore below is not needed -- the
        // same reason lure moved. #841 taught the proxy to send invasion there, behind two gates this
        // type alone has (the server declares grunt_type, and its own grunt masterdata lists the name),
        // but this call was never made, so every invasion edit still took the v1 path. When either gate
        // says no -- metal, kecleon, gold-stop, showcase, a stored gender 3, a 5.2.1 or 5.1.0 server --
        // the proxy answers null and the v1 path below runs: the shared upsert where the key is gone,
        // the natural-key replace where it is not.
        if (await TrackingV2Replacement.TryApplyAsync(
                this._proxy, TrackingType, userId, oldUid, body, this._uidRemapper) is { } v2Uid)
        {
            model.Uid = v2Uid;
            return model;
        }

        if (!keyEnforced)
        {
            // Nothing to free, so no delete-first and no window in which the alarm exists nowhere: the
            // create carrying the uid is resolved by PoracleNG's diff like any other type's, and the
            // reconciler drops the superseded row when one was inserted.
            var result = await this._proxy.CreateAsync(TrackingType, userId, body);
            model.Uid = await TrackingUpdateReconciler.ReconcileAsync(
                this._proxy, TrackingType, userId, oldUid, result, this._logger, body, this._uidRemapper);
            return model;
        }

        var original = oldUid > 0 ? await this.GetByUidAsync(userId, oldUid) : null;

        model.Uid = await NaturalKeyTrackingUpdate.ReplaceAsync(
            this._proxy,
            TrackingType,
            userId,
            oldUid,
            original is null ? null : SerializeToElement(original),
            body,
            this._logger,
            this._uidRemapper);

        return model;
    }

    public async Task<bool> DeleteAsync(string userId, int uid)
    {
        await this._proxy.DeleteByUidAsync(TrackingType, userId, uid);
        return true;
    }

    public async Task<int> DeleteAllByUserAsync(string userId, int profileNo)
    {
        var items = await this.ReadOwnRowsAsync(userId);
        var uids = items.Select(x => x.Uid).ToList();

        if (uids.Count == 0)
        {
            return 0;
        }

        await this._proxy.BulkDeleteByUidsAsync(TrackingType, userId, uids);
        return uids.Count;
    }

    public async Task<DistanceUpdateResult> UpdateDistanceByUserAsync(string userId, int profileNo, int distance)
    {
        var json = await this._proxy.GetByUserAsync(TrackingType, userId);
        var isOurs = await this.OwnRowPredicateAsync();
        // The stored rows are rewritten in place rather than round-tripped through the typed model,
        // so fields PoracleWeb does not model survive the write-back. See #730.
        var (body, skipped) = DistanceRewrite.Build(json, isOurs, distance);
        var count = body.GetArrayLength();

        if (count == 0)
        {
            return skipped;
        }
        // Two selected rows that differed only by radius become the same alarm once both are set to
        // the same one, and PoracleNG resolves that inside the batch -- fewer alarms than selected,
        // one left at its old radius, and a response claiming all were updated. See #580.
        TrackingUpdateReconciler.EnsureBatchDoesNotCollapse(body, TrackingType);

        // And against the rows NOT selected: at the new radius a selected row can differ from an
        // unselected sibling by exactly one updatable field, and PoracleNG then rewrites the SIBLING
        // -- an alarm the user never touched -- while the selected one keeps its old radius and the
        // response claims it was updated. See #598.
        await TrackingUpdateReconciler.EnsureBatchDoesNotTakeOverOthersAsync(
            this._proxy, TrackingType, userId, body);

        await this._proxy.CreateAsync(TrackingType, userId, body);
        // PoracleNG rewrites every row, so the uids change. Follow any quick-pick that
        // tracks them, pairing on content because the batch response is reordered. See #443.
        await BulkUidRemap.ApplyAsync(
            this._proxy, TrackingType, userId, body, this._uidRemapper, this._logger);

        return skipped with { Updated = count };
    }

    public async Task<DistanceUpdateResult> UpdateDistanceByUidsAsync(List<int> uids, string userId, int distance)
    {
        var json = await this._proxy.GetByUserAsync(TrackingType, userId);
        // The stored rows are rewritten in place rather than round-tripped through the typed model,
        // so fields PoracleWeb does not model survive the write-back. See #730.
        var selected = new HashSet<int>(uids);
        var isOurs = await this.OwnRowPredicateAsync();
        var (body, skipped) = DistanceRewrite.Build(
            json,
            row => isOurs(row) && PoracleJsonHelper.UidOf(row) is int rowUid && selected.Contains(rowUid),
            distance);
        var count = body.GetArrayLength();

        if (count == 0)
        {
            return skipped;
        }
        // Two selected rows that differed only by radius become the same alarm once both are set to
        // the same one, and PoracleNG resolves that inside the batch -- fewer alarms than selected,
        // one left at its old radius, and a response claiming all were updated. See #580.
        TrackingUpdateReconciler.EnsureBatchDoesNotCollapse(body, TrackingType);

        // And against the rows NOT selected: at the new radius a selected row can differ from an
        // unselected sibling by exactly one updatable field, and PoracleNG then rewrites the SIBLING
        // -- an alarm the user never touched -- while the selected one keeps its old radius and the
        // response claims it was updated. See #598.
        await TrackingUpdateReconciler.EnsureBatchDoesNotTakeOverOthersAsync(
            this._proxy, TrackingType, userId, body);

        await this._proxy.CreateAsync(TrackingType, userId, body);
        // PoracleNG rewrites every row, so the uids change. Follow any quick-pick that
        // tracks them, pairing on content because the batch response is reordered. See #443.
        await BulkUidRemap.ApplyAsync(
            this._proxy, TrackingType, userId, body, this._uidRemapper, this._logger);

        return skipped with { Updated = count };
    }

    public async Task<int> CountByUserAsync(string userId, int profileNo)
    {
        var items = await this.ReadOwnRowsAsync(userId);
        return items.Count;
    }

    public async Task<IEnumerable<Invasion>> BulkCreateAsync(string userId, IEnumerable<Invasion> models)
    {
        await this._featureGate.EnsureEnabledAsync(DisableFeatureKeys.Invasions);
        var modelList = models.ToList();

        foreach (var model in modelList)
        {
            model.Id = userId;
            RequireGruntType(model);
        }

        var body = SerializeToElement(modelList);
        var result = await this._proxy.CreateAsync(TrackingType, userId, body);

        for (var i = 0; i < modelList.Count && i < result.NewUids.Count; i++)
        {
            modelList[i].Uid = (int)result.NewUids[i];
        }

        return modelList;
    }

    /// <summary>
    /// PoracleNG rejects an empty <c>grunt_type</c> with <c>400 "Grunt type mandatory"</c> and has no
    /// catch-all keyword, so coalescing a missing value to <c>""</c> guaranteed a failure that surfaced
    /// as a generic 500. Fail here instead, where the message says what is actually wrong. Callers that
    /// want "everything" must fan out over <see cref="InvasionGruntTypes.All"/>. See #416.
    /// </summary>
    /// <summary>
    /// The grunt_type column width upstream: <c>varchar(255)</c>, per PoracleNG's initial schema
    /// migration at the commit production runs. This said 35, which was an invented limit wearing a
    /// factual justification -- in a fix whose whole point was refusing the impossible rather than
    /// allowing only the known. See #661.
    /// </summary>
    private const int MaxGruntTypeLength = 255;

    private static void RequireGruntType(Invasion model)
    {
        // Deliberately NOT an allowlist. The live database holds grunt types this codebase does not model --
        // blanche, candela, spark, "npc 0" through "npc 10", "player team leader" -- so validating against
        // InvasionGruntTypes.All would have refused edits to alarms that work today. What is checked instead
        // is what cannot be a grunt type under any upstream: control characters, and a value longer than the
        // column. See #611.
        if (!string.IsNullOrEmpty(model.GruntType))
        {
            if (model.GruntType.Any(char.IsControl))
            {
                throw new AlarmValidationException("gruntType must not contain control characters.");
            }

            if (model.GruntType.Length > MaxGruntTypeLength)
            {
                throw new AlarmValidationException(
                    $"gruntType must be {MaxGruntTypeLength} characters or fewer.");
            }
        }

        if (string.IsNullOrWhiteSpace(model.GruntType))
        {
            // AlarmValidationException rather than ArgumentException: nothing maps the latter, so this
            // message -- written precisely to explain the problem -- came back as a bare 500 on the
            // update path while the create path answered 400. See #518.
            throw new AlarmValidationException(
                "grunt_type is required — PoracleNG has no catch-all value. To track everything, "
                + "create one alarm per InvasionGruntTypes.All entry.");
        }
    }

    private static List<Invasion> DeserializeItems(JsonElement json) =>
        PoracleJsonHelper.DeserializeList<Invasion>(json);

    private static JsonElement SerializeToElement<T>(T value) =>
        PoracleJsonHelper.SerializeToElement(value);
}
