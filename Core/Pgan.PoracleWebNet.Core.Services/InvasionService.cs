using System.Text.Json;
using Microsoft.Extensions.Logging;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

public partial class InvasionService(IPoracleTrackingProxy proxy, IFeatureGate featureGate, ILogger<InvasionService> logger, ITrackedUidRemapper uidRemapper) : IInvasionService
{
    private const string TrackingType = "invasion";
    private readonly IPoracleTrackingProxy _proxy = proxy;
    private readonly IFeatureGate _featureGate = featureGate;
    private readonly ILogger<InvasionService> _logger = logger;
    private readonly ITrackedUidRemapper _uidRemapper = uidRemapper;

    public async Task<IEnumerable<Invasion>> GetByUserAsync(string userId, int profileNo)
    {
        var json = await this._proxy.GetByUserAsync(TrackingType, userId);
        return DeserializeItems(json);
    }

    public async Task<Invasion?> GetByUidAsync(string userId, int uid)
    {
        var json = await this._proxy.GetByUserAsync(TrackingType, userId);
        var items = DeserializeItems(json);
        return items.FirstOrDefault(x => x.Uid == uid);
    }

    public async Task<Invasion> CreateAsync(string userId, Invasion model)
    {
        await this._featureGate.EnsureEnabledAsync(DisableFeatureKeys.Invasions);
        model.Id = userId;
        RequireGruntType(model);

        // PoracleNG's natural key on (id, profile_no, gender, grunt_type) is case-insensitive at the
        // database, so creating "Water" alongside an existing "water" hit a duplicate-key error and came
        // back as a 500. The update path already refuses this; the create path did not. See #500.
        var siblings = await this.GetByUserAsync(userId, model.ProfileNo);
        if (siblings.Any(x => x.Gender == model.Gender
            && string.Equals(x.GruntType, model.GruntType, StringComparison.OrdinalIgnoreCase)))
        {
            throw new TrackingConflictException(
                TrackingType,
                "You already have an invasion alarm for that grunt type and gender. Edit or remove that one instead.");
        }

        var body = SerializeToElement(model);
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

        // PoracleNG guards this type with a natural unique key and its create has no upsert path, so
        // changing a field outside that key collides (Error 1062) and returns 500. Replace the row instead.
        // Refuse a collision BEFORE the delete. PoracleNG dedups invasions on
        // (id, profile_no, gender, grunt_type), so editing one onto a pair another alarm already
        // holds made the replace merge into that alarm - this one deleted, the other one silently
        // overwritten. Changing the gender dropdown is enough to trigger it. See #462.
        if (oldUid > 0)
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

        var original = oldUid > 0 ? await this.GetByUidAsync(userId, oldUid) : null;

        var body = SerializeToElement(model);

        // Carry forward anything the stored row holds that the model does not declare. See #730.
        body = await TrackingFieldPreserver.PreserveStoredFieldsAsync(
            this._proxy, TrackingType, userId, oldUid, body);

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
        var json = await this._proxy.GetByUserAsync(TrackingType, userId);
        var items = DeserializeItems(json);
        var uids = items.Select(x => x.Uid).ToList();

        if (uids.Count == 0)
        {
            return 0;
        }

        await this._proxy.BulkDeleteByUidsAsync(TrackingType, userId, uids);
        return uids.Count;
    }

    public async Task<int> UpdateDistanceByUserAsync(string userId, int profileNo, int distance)
    {
        var json = await this._proxy.GetByUserAsync(TrackingType, userId);
        // The stored rows are rewritten in place rather than round-tripped through the typed model,
        // so fields PoracleWeb does not model survive the write-back. See #730.
        var body = PoracleJsonHelper.RewriteRows(json, _ => true, ("distance", distance));
        var count = body.GetArrayLength();

        if (count == 0)
        {
            return 0;
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

        return count;
    }

    public async Task<int> UpdateDistanceByUidsAsync(List<int> uids, string userId, int distance)
    {
        var json = await this._proxy.GetByUserAsync(TrackingType, userId);
        // The stored rows are rewritten in place rather than round-tripped through the typed model,
        // so fields PoracleWeb does not model survive the write-back. See #730.
        var selected = new HashSet<int>(uids);
        var body = PoracleJsonHelper.RewriteRows(
            json,
            row => PoracleJsonHelper.UidOf(row) is int rowUid && selected.Contains(rowUid),
            ("distance", distance));
        var count = body.GetArrayLength();

        if (count == 0)
        {
            return 0;
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

        return count;
    }

    public async Task<int> CountByUserAsync(string userId, int profileNo)
    {
        var json = await this._proxy.GetByUserAsync(TrackingType, userId);
        var items = DeserializeItems(json);
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
