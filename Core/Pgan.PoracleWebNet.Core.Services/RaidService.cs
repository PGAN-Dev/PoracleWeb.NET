using System.Text.Json;
using Microsoft.Extensions.Logging;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

public class RaidService(IPoracleTrackingProxy proxy, IFeatureGate featureGate, ILogger<RaidService> logger, ITrackedUidRemapper uidRemapper) : IRaidService
{
    private const string TrackingType = "raid";
    private readonly IPoracleTrackingProxy _proxy = proxy;
    private readonly IFeatureGate _featureGate = featureGate;
    private readonly ILogger<RaidService> _logger = logger;
    private readonly ITrackedUidRemapper _uidRemapper = uidRemapper;

    public async Task<IEnumerable<Raid>> GetByUserAsync(string userId, int profileNo)
    {
        var json = await this._proxy.GetByUserAsync(TrackingType, userId);
        return DeserializeItems(json);
    }

    public async Task<Raid?> GetByUidAsync(string userId, int uid)
    {
        var json = await this._proxy.GetByUserAsync(TrackingType, userId);
        var items = DeserializeItems(json);
        return items.FirstOrDefault(x => x.Uid == uid);
    }

    public async Task<Raid> CreateAsync(string userId, Raid model)
    {
        await this._featureGate.EnsureEnabledAsync(DisableFeatureKeys.Raids);
        model.Id = userId;

        // An Add that PoracleNG resolves into an update of an existing alarm takes that alarm over:
        // 201 Created, and the user quietly loses the one they had. See #561.
        await TrackingUpdateReconciler.EnsureNoMergeIntoAnotherAlarmAsync(
            this._proxy, TrackingType, userId, 0, SerializeToElement(model));
        var body = SerializeToElement(model);
        var result = await this._proxy.CreateAsync(TrackingType, userId, body);

        if (result.NewUids.Count == 0)
        {
            return model;
        }

        model.Uid = (int)result.NewUids[0];

        // Read back rather than echo. PoracleNG rewrites level to 9000 when the alarm names a specific
        // boss, so the response advertised a level the stored row does not have -- and the card the SPA
        // renders from it disagreed with the same alarm after a refresh. Same rule PUT /api/areas was
        // given in #476. See #523.
        return await this.GetByUidAsync(userId, model.Uid) ?? model;
    }

    public async Task<Raid> UpdateAsync(string userId, Raid model)
    {
        await this._featureGate.EnsureEnabledAsync(DisableFeatureKeys.Raids);
        var oldUid = model.Uid;
        var body = SerializeToElement(model);

        // Carry forward anything the stored row holds that the model does not declare. See #730.
        body = await TrackingFieldPreserver.PreserveStoredFieldsAsync(
            this._proxy, TrackingType, userId, model.Uid, body);

        // Refuse before writing: PoracleNG would satisfy this by merging into the other alarm and
        // the reconciler would then delete this one, losing a row the user never touched. See #531.
        await TrackingUpdateReconciler.EnsureNoMergeIntoAnotherAlarmAsync(
            this._proxy, TrackingType, userId, oldUid, body);

        var result = await this._proxy.CreateAsync(TrackingType, userId, body);

        // PoracleNG inserts instead of upserting when the edit changes a dedup-key field,
        // leaving the pre-edit row behind as a duplicate. Drop it and report the surviving uid.
        model.Uid = await TrackingUpdateReconciler.ReconcileAsync(
            this._proxy, TrackingType, userId, oldUid, result, this._logger, body, this._uidRemapper);

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

    public async Task<IEnumerable<Raid>> BulkCreateAsync(string userId, IEnumerable<Raid> models)
    {
        await this._featureGate.EnsureEnabledAsync(DisableFeatureKeys.Raids);
        var modelList = models.ToList();

        foreach (var model in modelList)
        {
            model.Id = userId;
        }

        var body = SerializeToElement(modelList);
        var result = await this._proxy.CreateAsync(TrackingType, userId, body);

        for (var i = 0; i < modelList.Count && i < result.NewUids.Count; i++)
        {
            modelList[i].Uid = (int)result.NewUids[i];
        }

        return modelList;
    }

    private static List<Raid> DeserializeItems(JsonElement json) =>
        PoracleJsonHelper.DeserializeList<Raid>(json);

    private static JsonElement SerializeToElement<T>(T value) =>
        PoracleJsonHelper.SerializeToElement(value);
}
