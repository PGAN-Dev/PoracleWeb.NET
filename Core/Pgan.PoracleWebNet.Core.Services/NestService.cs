using System.Text.Json;
using Microsoft.Extensions.Logging;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

public class NestService(IPoracleTrackingProxy proxy, IFeatureGate featureGate, ILogger<NestService> logger, ITrackedUidRemapper uidRemapper) : INestService
{
    private const string TrackingType = "nest";
    private readonly IPoracleTrackingProxy _proxy = proxy;
    private readonly IFeatureGate _featureGate = featureGate;
    private readonly ILogger<NestService> _logger = logger;
    private readonly ITrackedUidRemapper _uidRemapper = uidRemapper;

    public async Task<IEnumerable<Nest>> GetByUserAsync(string userId, int profileNo)
    {
        var json = await this._proxy.GetByUserAsync(TrackingType, userId);
        return DeserializeItems(json);
    }

    public async Task<Nest?> GetByUidAsync(string userId, int uid)
    {
        var json = await this._proxy.GetByUserAsync(TrackingType, userId);
        var items = DeserializeItems(json);
        return items.FirstOrDefault(x => x.Uid == uid);
    }

    public async Task<Nest> CreateAsync(string userId, Nest model)
    {
        await this._featureGate.EnsureEnabledAsync(DisableFeatureKeys.Nests);
        model.Id = userId;

        // An Add that PoracleNG resolves into an update of an existing alarm takes that alarm over:
        // 201 Created, and the user quietly loses the one they had. See #561.
        await TrackingUpdateReconciler.EnsureNoMergeIntoAnotherAlarmAsync(
            this._proxy, TrackingType, userId, 0, SerializeToElement(model));
        var body = SerializeToElement(model);
        var result = await this._proxy.CreateAsync(TrackingType, userId, body);

        if (result.NewUids.Count > 0)
        {
            model.Uid = (int)result.NewUids[0];
        }

        return model;
    }

    public async Task<Nest> UpdateAsync(string userId, Nest model)
    {
        await this._featureGate.EnsureEnabledAsync(DisableFeatureKeys.Nests);
        var oldUid = model.Uid;
        var body = SerializeToElement(model);

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
        var items = DeserializeItems(json);
        var itemList = items.ToList();

        if (itemList.Count == 0)
        {
            return 0;
        }

        foreach (var item in itemList)
        {
            item.Distance = distance;
        }

        var body = SerializeToElement(itemList);
        // Two selected rows that differed only by radius become the same alarm once both are set to
        // the same one, and PoracleNG resolves that inside the batch -- fewer alarms than selected,
        // one left at its old radius, and a response claiming all were updated. See #580.
        TrackingUpdateReconciler.EnsureBatchDoesNotCollapse(body, TrackingType);

        await this._proxy.CreateAsync(TrackingType, userId, body);
        // PoracleNG rewrites every row, so the uids change. Follow any quick-pick that
        // tracks them, pairing on content because the batch response is reordered. See #443.
        await BulkUidRemap.ApplyAsync(
            this._proxy, TrackingType, userId, body, this._uidRemapper, this._logger);

        return itemList.Count;
    }

    public async Task<int> UpdateDistanceByUidsAsync(List<int> uids, string userId, int distance)
    {
        var json = await this._proxy.GetByUserAsync(TrackingType, userId);
        var items = DeserializeItems(json);
        var matching = items.Where(x => uids.Contains(x.Uid)).ToList();

        if (matching.Count == 0)
        {
            return 0;
        }

        foreach (var item in matching)
        {
            item.Distance = distance;
        }

        var body = SerializeToElement(matching);
        // Two selected rows that differed only by radius become the same alarm once both are set to
        // the same one, and PoracleNG resolves that inside the batch -- fewer alarms than selected,
        // one left at its old radius, and a response claiming all were updated. See #580.
        TrackingUpdateReconciler.EnsureBatchDoesNotCollapse(body, TrackingType);

        await this._proxy.CreateAsync(TrackingType, userId, body);
        // PoracleNG rewrites every row, so the uids change. Follow any quick-pick that
        // tracks them, pairing on content because the batch response is reordered. See #443.
        await BulkUidRemap.ApplyAsync(
            this._proxy, TrackingType, userId, body, this._uidRemapper, this._logger);

        return matching.Count;
    }

    public async Task<int> CountByUserAsync(string userId, int profileNo)
    {
        var json = await this._proxy.GetByUserAsync(TrackingType, userId);
        var items = DeserializeItems(json);
        return items.Count;
    }

    public async Task<IEnumerable<Nest>> BulkCreateAsync(string userId, IEnumerable<Nest> models)
    {
        await this._featureGate.EnsureEnabledAsync(DisableFeatureKeys.Nests);
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

    private static List<Nest> DeserializeItems(JsonElement json) =>
        PoracleJsonHelper.DeserializeList<Nest>(json);

    private static JsonElement SerializeToElement<T>(T value) =>
        PoracleJsonHelper.SerializeToElement(value);
}
