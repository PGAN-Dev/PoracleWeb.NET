using System.Text.Json;
using Microsoft.Extensions.Logging;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

public class FortChangeService(IPoracleTrackingProxy proxy, IFeatureGate featureGate, ILogger<FortChangeService> logger, ITrackedUidRemapper uidRemapper) : IFortChangeService
{
    private const string TrackingType = "fort";
    private readonly IPoracleTrackingProxy _proxy = proxy;
    private readonly IFeatureGate _featureGate = featureGate;
    private readonly ILogger<FortChangeService> _logger = logger;
    private readonly ITrackedUidRemapper _uidRemapper = uidRemapper;

    public async Task<IEnumerable<FortChange>> GetByUserAsync(string userId, int profileNo)
    {
        var json = await this._proxy.GetByUserAsync(TrackingType, userId);
        return DeserializeItems(json);
    }

    public async Task<FortChange?> GetByUidAsync(string userId, int uid)
    {
        var json = await this._proxy.GetByUserAsync(TrackingType, userId);
        var items = DeserializeItems(json);
        return items.FirstOrDefault(x => x.Uid == uid);
    }

    public async Task<FortChange> CreateAsync(string userId, FortChange model)
    {
        await this._featureGate.EnsureEnabledAsync(DisableFeatureKeys.FortChanges);
        model.Id = userId;

        // PoracleNG's dedup key for this type ignores distance, so a create matching an existing alarm's
        // fort type, include-empty flag and change types OVERWRITES that alarm's radius instead of adding a
        // second one -- while PoracleWeb answered 201 with a fresh uid, so the user believed they had two
        // alarms and the configured radius was gone. Refuse it and say which alarm is in the way, the same
        // way the natural-key types do. See #502.
        var siblings = await this.GetByUserAsync(userId, model.ProfileNo);
        if (siblings.Any(x => SameDedupKey(x, model)))
        {
            throw new TrackingConflictException(
                TrackingType,
                "You already have a fort-change alarm for those settings. Edit its radius instead of adding another.");
        }

        var body = SerializeToElement(model);
        var result = await this._proxy.CreateAsync(TrackingType, userId, body);

        if (result.NewUids.Count > 0)
        {
            model.Uid = (int)result.NewUids[0];
        }

        return model;
    }

    public async Task<FortChange> UpdateAsync(string userId, FortChange model)
    {
        await this._featureGate.EnsureEnabledAsync(DisableFeatureKeys.FortChanges);
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

    public async Task<IEnumerable<FortChange>> BulkCreateAsync(string userId, IEnumerable<FortChange> models)
    {
        await this._featureGate.EnsureEnabledAsync(DisableFeatureKeys.FortChanges);
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

    /// <summary>
    /// Whether two fort-change alarms occupy the same slot as far as PoracleNG is concerned.
    /// </summary>
    /// <remarks>Distance is excluded deliberately: upstream ignores it when deduping. See #502.</remarks>
    private static bool SameDedupKey(FortChange existing, FortChange candidate) =>
        string.Equals(existing.FortType, candidate.FortType, StringComparison.OrdinalIgnoreCase)
        && existing.IncludeEmpty == candidate.IncludeEmpty
        && existing.ChangeTypes.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .SequenceEqual(
                candidate.ChangeTypes.OrderBy(x => x, StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);

    private static List<FortChange> DeserializeItems(JsonElement json) =>
        PoracleJsonHelper.DeserializeList<FortChange>(json);

    private static JsonElement SerializeToElement<T>(T value) =>
        PoracleJsonHelper.SerializeToElement(value);
}
