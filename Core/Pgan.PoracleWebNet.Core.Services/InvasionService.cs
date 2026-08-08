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
        await this._proxy.CreateAsync(TrackingType, userId, body);
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
        return matching.Count;
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
    private static void RequireGruntType(Invasion model)
    {
        if (string.IsNullOrWhiteSpace(model.GruntType))
        {
            throw new ArgumentException(
                "grunt_type is required — PoracleNG has no catch-all value. To track everything, "
                + "create one alarm per InvasionGruntTypes.All entry.",
                nameof(model));
        }
    }

    private static List<Invasion> DeserializeItems(JsonElement json) =>
        PoracleJsonHelper.DeserializeList<Invasion>(json);

    private static JsonElement SerializeToElement<T>(T value) =>
        PoracleJsonHelper.SerializeToElement(value);
}
