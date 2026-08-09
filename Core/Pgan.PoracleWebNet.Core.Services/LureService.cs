using System.Text.Json;
using Microsoft.Extensions.Logging;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

public class LureService(IPoracleTrackingProxy proxy, IFeatureGate featureGate, ILogger<LureService> logger, ITrackedUidRemapper uidRemapper) : ILureService
{
    private const string TrackingType = "lure";
    private readonly IPoracleTrackingProxy _proxy = proxy;
    private readonly IFeatureGate _featureGate = featureGate;
    private readonly ILogger<LureService> _logger = logger;
    private readonly ITrackedUidRemapper _uidRemapper = uidRemapper;

    public async Task<IEnumerable<Lure>> GetByUserAsync(string userId, int profileNo)
    {
        var json = await this._proxy.GetByUserAsync(TrackingType, userId);
        return DeserializeItems(json);
    }

    public async Task<Lure?> GetByUidAsync(string userId, int uid)
    {
        var json = await this._proxy.GetByUserAsync(TrackingType, userId);
        var items = DeserializeItems(json);
        return items.FirstOrDefault(x => x.Uid == uid);
    }

    public async Task<Lure> CreateAsync(string userId, Lure model)
    {
        await this._featureGate.EnsureEnabledAsync(DisableFeatureKeys.Lures);
        model.Id = userId;

        // PoracleNG guards this type with a unique key on (id, profile_no, lure_id), so adding a lure
        // type already tracked hit a duplicate-key error and surfaced as a 500 -- for a submission the
        // lure picker actively invites, and which the frontend then reported as a generic "failed to
        // create" with no clue which lure caused it. The update path has refused this since #462.
        // See #562.
        var siblings = await this.GetByUserAsync(userId, model.ProfileNo);
        if (siblings.Any(x => x.LureId == model.LureId))
        {
            throw new TrackingConflictException(
                TrackingType,
                "You already have a lure alarm for that lure type. Edit or remove that one instead.");
        }

        var body = SerializeToElement(model);
        var result = await this._proxy.CreateAsync(TrackingType, userId, body);

        if (result.NewUids.Count > 0)
        {
            model.Uid = (int)result.NewUids[0];
        }

        return model;
    }

    public async Task<Lure> UpdateAsync(string userId, Lure model)
    {
        await this._featureGate.EnsureEnabledAsync(DisableFeatureKeys.Lures);
        var oldUid = model.Uid;

        // PoracleNG guards this type with a natural unique key and its create has no upsert path, so
        // changing a field outside that key collides (Error 1062) and returns 500. Replace the row instead.
        // Refuse a collision BEFORE the delete. PoracleNG dedups lures on (id, profile_no, lure_id),
        // so editing one onto a lure_id another alarm already holds made the replace merge into that
        // alarm - this one deleted, the other one silently overwritten. See #462.
        if (oldUid > 0)
        {
            var siblings = await this.GetByUserAsync(userId, model.ProfileNo);
            if (siblings.Any(x => x.Uid != oldUid && x.LureId == model.LureId))
            {
                throw new TrackingConflictException(
                    TrackingType,
                    "You already have a lure alarm for that lure type. Edit or remove that one instead.");
            }
        }

        var original = oldUid > 0 ? await this.GetByUidAsync(userId, oldUid) : null;

        model.Uid = await NaturalKeyTrackingUpdate.ReplaceAsync(
            this._proxy,
            TrackingType,
            userId,
            oldUid,
            original is null ? null : SerializeToElement(original),
            SerializeToElement(model),
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

    public async Task<IEnumerable<Lure>> BulkCreateAsync(string userId, IEnumerable<Lure> models)
    {
        await this._featureGate.EnsureEnabledAsync(DisableFeatureKeys.Lures);
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

    private static List<Lure> DeserializeItems(JsonElement json) =>
        PoracleJsonHelper.DeserializeList<Lure>(json);

    private static JsonElement SerializeToElement<T>(T value) =>
        PoracleJsonHelper.SerializeToElement(value);
}
