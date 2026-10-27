using System.Text.Json;
using Microsoft.Extensions.Logging;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

public class QuestService(
    IPoracleTrackingProxy proxy,
    IFeatureGate featureGate,
    ILogger<QuestService> logger,
    ITrackedUidRemapper uidRemapper) : IQuestService
{
    private const string TrackingType = "quest";
    private readonly IPoracleTrackingProxy _proxy = proxy;
    private readonly IFeatureGate _featureGate = featureGate;
    private readonly ILogger<QuestService> _logger = logger;
    private readonly ITrackedUidRemapper _uidRemapper = uidRemapper;

    public async Task<IEnumerable<Quest>> GetByUserAsync(string userId, int profileNo)
    {
        var json = await this._proxy.GetByUserAsync(TrackingType, userId);
        return DeserializeItems(json);
    }

    public async Task<Quest?> GetByUidAsync(string userId, int uid)
    {
        var json = await this._proxy.GetByUserAsync(TrackingType, userId);
        var items = DeserializeItems(json);
        return items.FirstOrDefault(x => x.Uid == uid);
    }

    public async Task<Quest> CreateAsync(string userId, Quest model)
    {
        await this._featureGate.EnsureEnabledAsync(DisableFeatureKeys.Quests);
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

    public async Task<Quest> UpdateAsync(string userId, Quest model)
    {
        await this._featureGate.EnsureEnabledAsync(DisableFeatureKeys.Quests);
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

    public async Task<IEnumerable<Quest>> BulkCreateAsync(string userId, IEnumerable<Quest> models) =>
        (await this.CreateBatchAsync(userId, [.. models])).Alarms;

    public async Task<BatchCreateResult<Quest>> CreateBatchAsync(string userId, IReadOnlyList<Quest> models)
    {
        await this._featureGate.EnsureEnabledAsync(DisableFeatureKeys.Quests);
        List<Quest> modelList = [.. models];

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

        return new BatchCreateResult<Quest>(
            modelList, result.TakeoversSkipped, result.TakeoversSkipped > 0 ? PoracleTrackingProxy.TakeoverMessage : null);
    }

    private static List<Quest> DeserializeItems(JsonElement json) =>
        PoracleJsonHelper.DeserializeList<Quest>(json);

    private static JsonElement SerializeToElement<T>(T value) =>
        PoracleJsonHelper.SerializeToElement(value);
}
