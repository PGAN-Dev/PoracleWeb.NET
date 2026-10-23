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

        var body = SerializeToElement(model);

        var result = await this._proxy.CreateAsync(TrackingType, userId, body);

        model.ExistingUid = result.ExistingUid;

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
        var body = SerializeToElement(model);

        // Carry forward anything the stored row holds that the model does not declare. See #730.
        body = await TrackingFieldPreserver.PreserveStoredFieldsAsync(
            this._proxy, TrackingType, userId, oldUid, body);

        model.Uid = await TrackingV2Replacement.ApplyAsync(
            this._proxy, TrackingType, userId, oldUid, body, this._uidRemapper);

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

    public async Task<DistanceUpdateResult> UpdateDistanceByUserAsync(string userId, int profileNo, int distance)
    {
        var json = await this._proxy.GetByUserAsync(TrackingType, userId);

        // One v2 PATCH naming the uids and the radius: nothing else a rule holds is re-sent (#730), each keeps
        // its uid, and PoracleNG refuses a rule that would collide with another rather than merging it.
        return await DistanceRewrite.ApplyAsync(this._proxy, TrackingType, userId, json, _ => true, distance, this._logger);
    }

    public async Task<DistanceUpdateResult> UpdateDistanceByUidsAsync(List<int> uids, string userId, int distance)
    {
        var json = await this._proxy.GetByUserAsync(TrackingType, userId);
        var selected = new HashSet<int>(uids);

        return await DistanceRewrite.ApplyAsync(
            this._proxy,
            TrackingType,
            userId,
            json,
            row => PoracleJsonHelper.UidOf(row) is int rowUid && selected.Contains(rowUid),
            distance,
            this._logger);
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

        for (var i = 0; i < modelList.Count && i < result.RowUids.Count; i++)
        {
            modelList[i].Uid = result.RowUids[i];
        }

        return modelList;
    }

    private static List<Lure> DeserializeItems(JsonElement json) =>
        PoracleJsonHelper.DeserializeList<Lure>(json);

    private static JsonElement SerializeToElement<T>(T value) =>
        PoracleJsonHelper.SerializeToElement(value);
}
