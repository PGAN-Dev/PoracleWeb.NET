using System.Text.Json;
using Microsoft.Extensions.Logging;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

public class EggService(IPoracleTrackingProxy proxy, IFeatureGate featureGate, ILogger<EggService> logger, ITrackedUidRemapper uidRemapper) : IEggService
{
    private const string TrackingType = "egg";
    private readonly IPoracleTrackingProxy _proxy = proxy;
    private readonly IFeatureGate _featureGate = featureGate;
    private readonly ILogger<EggService> _logger = logger;
    private readonly ITrackedUidRemapper _uidRemapper = uidRemapper;

    public async Task<IEnumerable<Egg>> GetByUserAsync(string userId, int profileNo)
    {
        var json = await this._proxy.GetByUserAsync(TrackingType, userId);
        return DeserializeItems(json);
    }

    public async Task<Egg?> GetByUidAsync(string userId, int uid)
    {
        var json = await this._proxy.GetByUserAsync(TrackingType, userId);
        var items = DeserializeItems(json);
        return items.FirstOrDefault(x => x.Uid == uid);
    }

    public async Task<Egg> CreateAsync(string userId, Egg model)
    {
        // Eggs intentionally share the disable_raids toggle — no separate disable_eggs key exists
        // (eggs and raids share UI in the SPA). See DisableFeatureKeys.Raids comment.
        await this._featureGate.EnsureEnabledAsync(DisableFeatureKeys.Raids);
        model.Id = userId;

        var body = SerializeToElement(model);
        var result = await this._proxy.CreateAsync(TrackingType, userId, body);

        if (result.NewUids.Count > 0)
        {
            model.Uid = (int)result.NewUids[0];
        }

        return model;
    }

    public async Task<Egg> UpdateAsync(string userId, Egg model)
    {
        await this._featureGate.EnsureEnabledAsync(DisableFeatureKeys.Raids);
        var oldUid = model.Uid;
        var body = SerializeToElement(model);

        // Carry forward anything the stored row holds that the model does not declare. See #730.
        body = await TrackingFieldPreserver.PreserveStoredFieldsAsync(
            this._proxy, TrackingType, userId, model.Uid, body);

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

    public async Task<IEnumerable<Egg>> BulkCreateAsync(string userId, IEnumerable<Egg> models)
    {
        // Eggs intentionally share the disable_raids toggle (see CreateAsync comment).
        await this._featureGate.EnsureEnabledAsync(DisableFeatureKeys.Raids);
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

    private static List<Egg> DeserializeItems(JsonElement json) =>
        PoracleJsonHelper.DeserializeList<Egg>(json);

    private static JsonElement SerializeToElement<T>(T value) =>
        PoracleJsonHelper.SerializeToElement(value);
}
