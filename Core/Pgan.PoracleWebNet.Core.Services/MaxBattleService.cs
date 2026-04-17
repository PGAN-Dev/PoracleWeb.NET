using System.Text.Json;
using Microsoft.Extensions.Logging;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

public partial class MaxBattleService(IPoracleTrackingProxy proxy, IFeatureGate featureGate, ILogger<MaxBattleService> logger) : IMaxBattleService
{
    private const string TrackingType = "maxbattle";
    private readonly ILogger<MaxBattleService> _logger = logger;
    private readonly IPoracleTrackingProxy _proxy = proxy;
    private readonly IFeatureGate _featureGate = featureGate;

    public async Task<IEnumerable<MaxBattle>> GetByUserAsync(string userId, int profileNo)
    {
        var json = await this._proxy.GetByUserAsync(TrackingType, userId);
        return DeserializeItems(json);
    }

    public async Task<MaxBattle?> GetByUidAsync(string userId, int uid)
    {
        var json = await this._proxy.GetByUserAsync(TrackingType, userId);
        var items = DeserializeItems(json);
        return items.FirstOrDefault(x => x.Uid == uid);
    }

    public async Task<MaxBattle> CreateAsync(string userId, MaxBattle model)
    {
        await this._featureGate.EnsureEnabledAsync(DisableFeatureKeys.MaxBattles);
        model.Id = userId;
        var body = SerializeToElement(model);
        var result = await this._proxy.CreateAsync(TrackingType, userId, body);

        if (result.NewUids.Count > 0)
        {
            model.Uid = (int)result.NewUids[0];
        }

        return model;
    }

    public async Task<MaxBattle> UpdateAsync(string userId, MaxBattle model)
    {
        await this._featureGate.EnsureEnabledAsync(DisableFeatureKeys.MaxBattles);
        // MaxBattle is insert-only in PoracleNG (no dedup/upsert).
        // Delete the old alarm first, then create a replacement.
        await this._proxy.DeleteByUidAsync(TrackingType, userId, model.Uid);

        var body = SerializeToElement(model);
        var result = await this._proxy.CreateAsync(TrackingType, userId, body);

        if (result.NewUids.Count > 0)
        {
            model.Uid = (int)result.NewUids[0];
        }

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

        // MaxBattle is insert-only — bulk delete then re-create with updated distance.
        // If the re-create fails after delete, alarms are lost. Log for recovery.
        var uids = itemList.Select(x => x.Uid).ToList();
        await this._proxy.BulkDeleteByUidsAsync(TrackingType, userId, uids);

        foreach (var item in itemList)
        {
            item.Distance = distance;
        }

        try
        {
            var body = SerializeToElement(itemList);
            await this._proxy.CreateAsync(TrackingType, userId, body);
        }
        catch (Exception ex)
        {
            LogRecreateFailed(this._logger, ex,
                itemList.Count, userId, string.Join(", ", uids));
            throw;
        }

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

        // MaxBattle is insert-only — bulk delete then re-create with updated distance.
        var matchingUids = matching.Select(x => x.Uid).ToList();
        await this._proxy.BulkDeleteByUidsAsync(TrackingType, userId, matchingUids);

        foreach (var item in matching)
        {
            item.Distance = distance;
        }

        try
        {
            var body = SerializeToElement(matching);
            await this._proxy.CreateAsync(TrackingType, userId, body);
        }
        catch (Exception ex)
        {
            LogRecreateFailed(this._logger, ex,
                matching.Count, userId, string.Join(", ", matchingUids));
            throw;
        }

        return matching.Count;
    }

    public async Task<int> CountByUserAsync(string userId, int profileNo)
    {
        var json = await this._proxy.GetByUserAsync(TrackingType, userId);
        var items = DeserializeItems(json);
        return items.Count;
    }

    public async Task<IEnumerable<MaxBattle>> BulkCreateAsync(string userId, IEnumerable<MaxBattle> models)
    {
        await this._featureGate.EnsureEnabledAsync(DisableFeatureKeys.MaxBattles);
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

    private static List<MaxBattle> DeserializeItems(JsonElement json) =>
        PoracleJsonHelper.DeserializeList<MaxBattle>(json);

    private static JsonElement SerializeToElement<T>(T value) =>
        PoracleJsonHelper.SerializeToElement(value);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to re-create {Count} maxbattle alarms after bulk delete for user {UserId}. Alarms with UIDs [{Uids}] were deleted but not re-created")]
    private static partial void LogRecreateFailed(ILogger logger, Exception ex, int count, string userId, string uids);
}
